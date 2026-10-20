using System.Globalization;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Reporting;
using EplanMcp.Evals.Run;
using EplanMcp.Evals.Server;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals;

/// <summary>
/// <c>run</c>: the preflight, then every selected task against EPLAN and the model, then the report in
/// <c>.local/evals/&lt;yyyyMMdd-HHmmss&gt;/</c> (agent evals design 4.2 to 4.4). A cancelled run (Ctrl+C) still writes
/// the report of what it finished.
/// </summary>
internal static class RunCommand
{
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
            string mcpConfig = Path.Combine(run, "mcp.json");
            await File.WriteAllTextAsync(mcpConfig, ClaudeCommand.McpConfig(eplanmcp), cancellationToken).ConfigureAwait(false);
            string workRoot = Path.Combine(Path.GetTempPath(), "eplanmcp-evals", name);
            var attempts = new ClaudeAttempts(new ClaudeSetup(claude, options.Model, mcpConfig), run, workRoot, options.Repeat > 1);

            var records = new List<TaskRecord>();
            int exit = 0;
            try
            {
                await EvalRun.RunAsync(tasks, options.Repeat, client, tools, attempts, output, records, EvalRun.RetryDelay, cancellationToken).ConfigureAwait(false);
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
            return exit;
        }
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
