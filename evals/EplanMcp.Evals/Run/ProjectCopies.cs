using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Server;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Run;

/// <summary>
/// The fresh copies of a run (M3 design 7 and Q4): one copy of EPLAN's sample project per attempt of each task on a
/// copy, under <c>&lt;scratch root&gt;\evals\&lt;run&gt;\</c>, each with a project name of its own for the prompt.
/// Copies in <c>copies\</c> lie in the only scratch folder of the evals' eplanmcp (<see cref="EvalsHome"/>); copies in
/// <c>outside\</c> do not, so every write to them is refused. The engineer opens the copies in EPLAN, and the run
/// waits until EPLAN lists them all (evals/README.md).
/// </summary>
internal static class ProjectCopies
{
    /// <summary>Where EPLAN keeps its sample project, in folders named in the language EPLAN was installed in (eval-fixture.ps1).</summary>
    public const string DataRoot = @"C:\Users\Public\EPLAN\Data";

    public const string SampleProject = "EPLAN_Sample_Project.elk";

    /// <summary>The folder of the copies the model may write to: the evals' eplanmcp's only scratch folder.</summary>
    public static string Writable(string runRoot) => Path.Combine(runRoot, "copies");

    /// <summary>The sample project's .elk file under <paramref name="dataRoot"/>; null when there is none.</summary>
    public static string? FindTemplate(string dataRoot) =>
        Directory.Exists(dataRoot)
            ? Directory.EnumerateFiles(dataRoot, SampleProject, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 })
                .Order(StringComparer.Ordinal).FirstOrDefault()
            : null;

    /// <summary>
    /// Copies <paramref name="template"/>, its .elk file and .edb folder (api-notes A12), once per attempt of each task on a
    /// copy. The n-th copy of the run is <c>Eval_&lt;run&gt;_&lt;nn&gt;</c> in a folder <c>nn</c> of its own.
    /// </summary>
    public static Dictionary<(string Task, int Attempt), AttemptCopy> Create(IEnumerable<EvalTask> tasks, int repeat, string template, string runRoot)
    {
        var copies = new Dictionary<(string Task, int Attempt), AttemptCopy>();
        string source = Path.ChangeExtension(template, null);
        foreach (EvalTask task in tasks.Where(t => t.Fixture != TaskFixture.Shared))
        {
            for (int attempt = 1; attempt <= repeat; attempt++)
            {
                string number = (copies.Count + 1).ToString("00", CultureInfo.InvariantCulture);
                string name = $"Eval_{Path.GetFileName(runRoot).Replace('-', '_')}_{number}";
                string folder = task.Fixture == TaskFixture.Copy ? Writable(runRoot) : Path.Combine(runRoot, "outside");
                string path = Path.Combine(folder, number, name);
                try
                {
                    CopyProject(source, path);
                    copies[(task.Id, attempt)] = new AttemptCopy(name, path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    copies[(task.Id, attempt)] = new AttemptCopy(name, path, "copying the sample project failed: " + ex.Message);
                }
            }
        }

        return copies;
    }

    /// <summary>
    /// Waits until EPLAN lists every copy among its open projects and none of them is the current one, so the tasks on
    /// the shared fixture keep reading the fixture. A copy not open within <paramref name="limit"/> gets a problem.
    /// </summary>
    public static async Task<Dictionary<(string Task, int Attempt), AttemptCopy>> WaitUntilOpenAsync(
        IToolClient client,
        Dictionary<(string Task, int Attempt), AttemptCopy> copies,
        TextWriter progress,
        TimeSpan limit,
        TimeSpan poll,
        CancellationToken cancellationToken)
    {
        var waiting = copies.Where(c => c.Value.Problem is null).ToDictionary(c => c.Key, c => c.Value);
        var clock = Stopwatch.StartNew();
        string? shown = null;
        while (waiting.Count > 0)
        {
            PagedOutcome projects = await ToolPages.AllAsync(client, "eplan_project_list", new JsonObject { ["limit"] = 200 }, cancellationToken).ConfigureAwait(false);
            JsonObject[] open = [.. projects.Pages.SelectMany(p => p["projects"] as JsonArray ?? []).OfType<JsonObject>()];
            int opened = waiting.Values.Count(c => open.Any(p => Same(p["path"], c.Path)));
            bool copyIsCurrent = waiting.Values.Any(c => open.Any(p => Same(p["path"], c.Path) && p["current"]?.GetValueKind() == JsonValueKind.True));
            string state = string.Create(CultureInfo.InvariantCulture, $"{opened} of {waiting.Count} copies are open in EPLAN")
                + (copyIsCurrent ? "; a copy is the current project: select the fixture in the page navigator again" : string.Empty);
            if (state != shown)
            {
                await progress.WriteLineAsync(projects.Problem is { } problem ? $"{state} ({problem})" : state).ConfigureAwait(false);
                shown = state;
            }

            if ((opened == waiting.Count && !copyIsCurrent) || clock.Elapsed >= limit)
            {
                foreach ((var key, AttemptCopy copy) in waiting.Where(c => !open.Any(p => Same(p["path"], c.Value.Path))))
                {
                    copies[key] = copy with { Problem = string.Create(CultureInfo.InvariantCulture, $"it was not opened in EPLAN within {limit.TotalMinutes} minutes") };
                }

                return copies;
            }

            await Task.Delay(poll, cancellationToken).ConfigureAwait(false);
        }

        return copies;
    }

    private static void CopyProject(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source + ".elk", target + ".elk");
        string edb = source + ".edb";
        Directory.CreateDirectory(target + ".edb");
        foreach (string directory in Directory.EnumerateDirectories(edb, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(target + ".edb" + directory[edb.Length..]);
        }

        foreach (string file in Directory.EnumerateFiles(edb, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, target + ".edb" + file[edb.Length..]);
        }
    }

    /// <summary>A project path as eplan_project_list gives it (without .elk) against a copy's path.</summary>
    private static bool Same(JsonNode? listed, string path) =>
        Comparison.Text(listed) is { } text && string.Equals(Path.TrimEndingDirectorySeparator(text), path, StringComparison.OrdinalIgnoreCase);
}
