using System.Globalization;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Reporting;
using EplanMcp.Evals.Run;
using EplanMcp.Evals.Server;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals;

/// <summary>
/// <c>run</c>: the preflight, then every selected task against EPLAN and the model, then the report in
/// <c>.local/evals/&lt;yyyyMMdd-HHmmss&gt;/</c> (agent evals design 4.2 to 4.4). The model's eplanmcp gets the data
/// root <c>eplanmcp\</c> there (<see cref="EvalsHome"/>); tasks on a copy get their copies first. A cancelled run
/// (Ctrl+C) still writes the report of what it finished.
/// </summary>
internal static class RunCommand
{
    /// <summary>How long the engineer has to open the copies of a run in EPLAN.</summary>
    internal static readonly TimeSpan OpenLimit = TimeSpan.FromMinutes(20);

    /// <returns>0 when the run finished, 1 when it could not start or stopped on an error, 130 when it was cancelled.</returns>
    public static async Task<int> RunAsync(RunOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        string? root = ToolLocator.RepositoryRoot(Environment.CurrentDirectory);
        string directory = options.Tasks ?? Path.Combine(root ?? Environment.CurrentDirectory, "evals", "tasks");
        if (!Directory.Exists(directory))
        {
            return Stop(output, $"There is no task directory {directory}.");
        }

        IReadOnlyList<TaskFile> files = TaskLoader.LoadDirectory(directory);
        string[] invalidFiles = [.. files.SelectMany(f => f.Problems.Select(p => $"{f.Name}.yaml {p}"))];
        EvalTask[] tasks =
        [
            .. files.Select(f => f.Task).OfType<EvalTask>()
                .Where(t => options.SetToRun == "all" || (options.SetToRun == "dev") == (t.Set == TaskSet.Dev))
                .Where(t => options.Task is null || t.Id == options.Task),
        ];
        if (tasks.Length == 0)
        {
            return Stop(output, $"No valid task in {directory} matches --set {options.SetToRun}{(options.Task is null ? string.Empty : " --task " + options.Task)}; run check for the problems.");
        }

        string? template = null;
        if (tasks.Any(t => t.Fixture != TaskFixture.Shared))
        {
            template = options.Template ?? ProjectCopies.FindTemplate(ProjectCopies.DataRoot);
            if (template is null || !File.Exists(template))
            {
                return Stop(output, $"Tasks on a copy copy {ProjectCopies.SampleProject}, which is not under {ProjectCopies.DataRoot}{(template is null ? string.Empty : " or at " + template)}; pass --template.");
            }
        }

        string? claude = options.Claude ?? ToolLocator.Claude(
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        string? eplanmcp = options.Eplanmcp ?? ToolLocator.Eplanmcp(root);
        if (claude is null || eplanmcp is null)
        {
            return Stop(output, claude is null
                ? "claude.exe is neither on the PATH nor in the Claude desktop app; pass --claude."
                : "There is no eplanmcp build; run dotnet build src\\EplanMcp.Host -c Release, or pass --eplanmcp.");
        }

        decimal limit = tasks.Sum(t => t.Budget.MaxUsd) * options.Repeat;
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{tasks.Length} tasks x {options.Repeat} = {tasks.Length * options.Repeat} attempts with {options.Model}, spending at most {limit:0.00} USD; {invalidFiles.Length} problems in task files.")).ConfigureAwait(false);
        if (await Preflight.ClaudeLoginProblemAsync(claude, cancellationToken).ConfigureAwait(false) is { } login)
        {
            return Stop(output, "Preflight: " + login);
        }

        if (await TryStartAsync(eplanmcp, output, cancellationToken).ConfigureAwait(false) is not { } client)
        {
            return 1;
        }

        await using (client.ConfigureAwait(false))
        {
            if (await Preflight.EplanProblemAsync(client, Preflight.ScratchRoot(), cancellationToken).ConfigureAwait(false) is { } eplan)
            {
                return Stop(output, "Preflight: " + eplan);
            }

            HashSet<string> tools = [.. await client.ListToolsAsync(cancellationToken).ConfigureAwait(false)];
            DateTimeOffset started = DateTimeOffset.Now;
            string evals = Path.Combine(root ?? Environment.CurrentDirectory, ".local", "evals");
            string stamp = started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string name = stamp;
            for (int n = 2; Directory.Exists(Path.Combine(evals, name)); n++)
            {
                name = string.Create(CultureInfo.InvariantCulture, $"{stamp}-{n}");
            }

            string run = Directory.CreateDirectory(Path.Combine(evals, name)).FullName;
            string home = Path.Combine(run, "eplanmcp");
            string copiesRoot = Path.Combine(Preflight.ScratchRoot(), "evals", name);
            EvalsHome.Write(home, ProjectCopies.Writable(copiesRoot));
            string mcpConfig = Path.Combine(run, "mcp.json");
            await File.WriteAllTextAsync(mcpConfig, ClaudeCommand.McpConfig(eplanmcp, home), cancellationToken).ConfigureAwait(false);
            string workRoot = Path.Combine(Path.GetTempPath(), "eplanmcp-evals", name);
            var attempts = new ClaudeAttempts(new ClaudeSetup(claude, options.Model, mcpConfig), run, workRoot, options.Repeat > 1, EvalsHome.Exports(home));
            Dictionary<(string Task, int Attempt), AttemptCopy> copies = [];
            string copiesLeft = $"The copies stay open in EPLAN: close them there, then delete {copiesRoot}.";
            if (template is not null)
            {
                string? problem;
                try
                {
                    (copies, problem) = await OpenCopiesAsync(tasks, options.Repeat, template, copiesRoot, client, output, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await output.WriteLineAsync(copiesLeft).ConfigureAwait(false);
                    throw;
                }

                if (problem is not null)
                {
                    return Stop(output, copies.Count > 0 ? problem + " " + copiesLeft : problem);
                }
            }

            var records = new List<TaskRecord>();
            int exit = 0;
            try
            {
                await EvalRun.RunAsync(tasks, options.Repeat, client, tools, attempts, output, records, EvalRun.RetryDelay, copies, () => EvalsHome.AuditedWrites(home), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                exit = 130;
                await output.WriteLineAsync("Cancelled; the report covers what finished.").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Whatever broke (eplanmcp gone, a disk error), the attempts already paid for are still reported.
                exit = 1;
                await output.WriteLineAsync($"Stopped by an error, the report covers what finished: {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
            }
            finally
            {
                // The empty working directories of the attempts, under the system temp directory.
                if (Directory.Exists(workRoot))
                {
                    ClaudeAttempts.TryDelete(workRoot);
                }
            }

            var report = new RunReport(started, options.Model, claude, eplanmcp, invalidFiles, records);
            await ReportWriter.WriteAsync(report, run, CancellationToken.None).ConfigureAwait(false);
            foreach (SetSummary set in ReportWriter.Summarize(report))
            {
                await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{set.Set}: {set.Solved} of {set.Counted} attempts solved, {set.CostUsd:0.00} USD.")).ConfigureAwait(false);
            }

            await output.WriteLineAsync("Report: " + Path.Combine(run, ReportWriter.MarkdownFile)).ConfigureAwait(false);
            if (copies.Count > 0)
            {
                await output.WriteLineAsync(copiesLeft).ConfigureAwait(false);
            }

            return exit;
        }
    }

    /// <summary>
    /// Makes a fresh copy for every attempt of the tasks on a copy and waits until the engineer has opened them all in
    /// EPLAN and made the fixture, the project current at the preflight, current again: nothing the evals may use opens
    /// a project in EPLAN (evals/README.md). The problem says why the run must not go on.
    /// </summary>
    private static async Task<(Dictionary<(string Task, int Attempt), AttemptCopy> Copies, string? Problem)> OpenCopiesAsync(
        IReadOnlyList<EvalTask> tasks, int repeat, string template, string copiesRoot, IToolClient client, TextWriter output, CancellationToken cancellationToken)
    {
        string? fixture = await ProjectCopies.CurrentProjectAsync(client, cancellationToken).ConfigureAwait(false);
        if (fixture is null)
        {
            return ([], "EPLAN names no current project any more: open the fixture and start the run again.");
        }

        Dictionary<(string Task, int Attempt), AttemptCopy> copies = ProjectCopies.Create(tasks, repeat, template, copiesRoot);
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Open these {copies.Count} copies of {Path.GetFileName(template)} in EPLAN (File > Open), then select the fixture {Path.GetFileName(fixture)} in the page navigator again; waiting up to {OpenLimit.TotalMinutes} minutes, Ctrl+C stops:")).ConfigureAwait(false);
        foreach (AttemptCopy copy in copies.Values)
        {
            await output.WriteLineAsync($"  {copy.Path}.elk{(copy.Problem is null ? string.Empty : " (" + copy.Problem + ")")}").ConfigureAwait(false);
        }

        bool fixtureCurrent = await ProjectCopies.WaitUntilOpenAsync(client, copies, fixture, output, OpenLimit, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        return (copies, fixtureCurrent ? null : $"The fixture {fixture} is not EPLAN's current project, so the tasks on it would read another one: select it in the page navigator and start the run again.");
    }

    private static async Task<EplanMcpClient?> TryStartAsync(string eplanmcp, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            return await EplanMcpClient.StartAsync(eplanmcp, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Stop(output, $"Could not start eplanmcp at {eplanmcp}: {ex.Message}");
            return null;
        }
    }

    private static int Stop(TextWriter output, string message)
    {
        output.WriteLine(message);
        return 1;
    }
}
