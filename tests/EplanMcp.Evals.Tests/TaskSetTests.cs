using EplanMcp.Evals.Run;
using EplanMcp.Evals.Tasks;
using EplanMcp.Evals.Tests.Server;

namespace EplanMcp.Evals.Tests;

/// <summary>
/// Keeps the repository's task set (evals/tasks) runnable: every file valid, every oracle and after tool one that eplanmcp
/// lists. What the oracle calls return is checked on the fixture before a task is added (agent evals design 4.5).
/// </summary>
[Trait("Size", "Medium")]
public sealed class TaskSetTests(EplanMcpServerFixture server) : IClassFixture<EplanMcpServerFixture>
{
    private static readonly string _tasks = Path.Combine(ToolLocator.RepositoryRoot(AppContext.BaseDirectory)!, "evals", "tasks");

    // The Destructive tasks: an engineer approves in EPLAN, so they are run on purpose only (evals/README.md).
    private static readonly string _attended = Path.Combine(ToolLocator.RepositoryRoot(AppContext.BaseDirectory)!, "evals", "tasks-attended");

    private static IReadOnlyList<TaskFile> Files => TaskLoader.LoadDirectory(_tasks);

    private static IReadOnlyList<TaskFile> AttendedFiles => TaskLoader.LoadDirectory(_attended);

    [Fact]
    public void EveryTaskFile_IsValid()
    {
        Assert.All(Files.Concat(AttendedFiles), f => Assert.True(f.Problems.Count == 0, $"{f.Name}.yaml: {string.Join("; ", f.Problems)}"));
        Assert.Empty(Directory.EnumerateFiles(_tasks, "*.yml"));
        Assert.Empty(Directory.EnumerateFiles(_attended, "*.yml"));
    }

    [Fact]
    public void TheAttendedTasks_WriteOnlyToCopies_AndDifferFromTheOthers()
    {
        EvalTask[] attended = [.. AttendedFiles.Select(f => f.Task!)];

        Assert.NotEmpty(attended);
        Assert.All(attended, t => Assert.Equal(TaskFixture.Copy, t.Fixture));
        Assert.Empty(attended.Select(t => t.Id).Intersect(Files.Select(f => f.Task!.Id), StringComparer.Ordinal));
        Assert.Empty(attended.Select(t => t.Prompt).Intersect(Files.Select(f => f.Task!.Prompt), StringComparer.Ordinal));
    }

    [Fact]
    public void TheSets_HaveRoughlyTheSizesOfTheDesign()
    {
        EvalTask[] tasks = [.. Files.Select(f => f.Task!)];

        Assert.InRange(tasks.Count(t => t.Set == TaskSet.Dev), 12, 30);
        Assert.InRange(tasks.Count(t => t.Set == TaskSet.Holdout), 5, 10);
        Assert.Contains(tasks, t => t.Id == "holdout-action-try-safety");
        Assert.Equal(tasks.Length, tasks.Select(t => t.Prompt).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task EveryOracleTool_IsAToolOfEplanmcp()
    {
        IReadOnlyList<string> tools = await server.Client.ListToolsAsync(TestContext.Current.CancellationToken);

        // exported_files is the runner's own after call, not a tool of eplanmcp.
        Assert.All(
            Files.Concat(AttendedFiles).SelectMany(f => f.Task!.Oracle.Concat(f.Task.After).Select(o => (f.Name, o.Value.Tool))).Where(o => o.Tool != EvalTask.ExportedFiles),
            o => Assert.True(tools.Contains(o.Tool), $"{o.Name}.yaml calls {o.Tool}"));
    }
}
