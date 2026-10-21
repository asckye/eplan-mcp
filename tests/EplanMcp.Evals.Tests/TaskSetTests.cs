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

    private static IReadOnlyList<TaskFile> Files => TaskLoader.LoadDirectory(_tasks);

    [Fact]
    public void EveryTaskFile_IsValid()
    {
        Assert.All(Files, f => Assert.True(f.Problems.Count == 0, $"{f.Name}.yaml: {string.Join("; ", f.Problems)}"));
        Assert.Empty(Directory.EnumerateFiles(_tasks, "*.yml"));
    }

    [Fact]
    public void TheSets_HaveRoughlyTheSizesOfTheDesign()
    {
        EvalTask[] tasks = [.. Files.Select(f => f.Task!)];

        Assert.InRange(tasks.Count(t => t.Set == TaskSet.Dev), 12, 25);
        Assert.InRange(tasks.Count(t => t.Set == TaskSet.Holdout), 5, 10);
        Assert.Equal(tasks.Length, tasks.Select(t => t.Prompt).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task EveryOracleTool_IsAToolOfEplanmcp()
    {
        IReadOnlyList<string> tools = await server.Client.ListToolsAsync(TestContext.Current.CancellationToken);

        // exported_files is the runner's own after call, not a tool of eplanmcp.
        Assert.All(
            Files.SelectMany(f => f.Task!.Oracle.Concat(f.Task.After).Select(o => (f.Name, o.Value.Tool))).Where(o => o.Tool != EvalTask.ExportedFiles),
            o => Assert.True(tools.Contains(o.Tool), $"{o.Name}.yaml calls {o.Tool}"));
    }
}
