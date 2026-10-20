using EplanMcp.Evals.Run;

namespace EplanMcp.Evals.Tests.Run;

/// <summary>The ways a run stops before any attempt, with files in a temporary directory and no EPLAN.</summary>
[Trait("Size", "Medium")]
public sealed class RunCommandTests : IDisposable
{
    private const string PageCount = """
        id: page-count
        set: dev
        prompt: 这个项目有多少页？
        answer: { type: object, properties: { pages: { type: integer } } }
        oracle: { pages: { tool: eplan_project_get, pick: $.counts.pages } }
        checks: [{ answer: $.pages, equals: oracle.pages }]
        """;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-run-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task Run_WithoutTheTaskDirectory_Stops()
    {
        string missing = Path.Combine(_directory.FullName, "tasks");

        (int exit, string output) = await Run(RunOptions.Default with { Tasks = missing });

        Assert.Equal((1, $"There is no task directory {missing}.\n"), (exit, output));
    }

    [Fact]
    public async Task Run_WithNoMatchingTask_Stops()
    {
        File.WriteAllText(Path.Combine(_directory.FullName, "page-count.yaml"), PageCount);

        (int exit, string output) = await Run(RunOptions.Default with { Tasks = _directory.FullName, Set = "holdout" });

        Assert.Equal(1, exit);
        Assert.StartsWith($"No valid task in {_directory.FullName} matches --set holdout;", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_LoggedOut_StopsAtThePreflightAfterShowingThePlan()
    {
        File.WriteAllText(Path.Combine(_directory.FullName, "page-count.yaml"), PageCount);
        string claude = Path.Combine(_directory.FullName, "claude.cmd");
        File.WriteAllText(claude, "@echo off\r\nexit /b 1\r\n");

        (int exit, string output) = await Run(RunOptions.Default with { Tasks = _directory.FullName, Claude = claude, Eplanmcp = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe") });

        Assert.Equal(1, exit);
        Assert.Equal(
            "1 tasks x 1 = 1 attempts with claude-sonnet-5-5, spending at most 0.50 USD; 0 problems in task files.\n"
            + "Preflight: claude is not logged in: run `claude auth login` (or `claude setup-token`) once, then start the run again\n",
            output);
    }

    [Fact]
    public async Task Run_AnEplanmcpThatDoesNotStart_StopsWithItsPath()
    {
        File.WriteAllText(Path.Combine(_directory.FullName, "page-count.yaml"), PageCount);
        string claude = Path.Combine(_directory.FullName, "claude.cmd");
        File.WriteAllText(claude, "@echo off\r\nexit /b 0\r\n");
        string eplanmcp = Path.Combine(_directory.FullName, "missing", "eplanmcp.exe");

        (int exit, string output) = await Run(RunOptions.Default with { Tasks = _directory.FullName, Claude = claude, Eplanmcp = eplanmcp });

        Assert.Equal(1, exit);
        Assert.Contains($"\nCould not start eplanmcp at {eplanmcp}: ", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_ATaskOnACopyWithoutTheSampleProject_StopsBeforeThePreflight()
    {
        File.WriteAllText(Path.Combine(_directory.FullName, "rename.yaml"), """
            id: rename
            set: dev
            fixture: copy
            prompt: 在项目 {project} 中改名
            answer: { type: object, properties: { done: { type: boolean } } }
            checks: [{ answer: $.done, equals: true }]
            """);
        string template = Path.Combine(_directory.FullName, "missing.elk");

        (int exit, string output) = await Run(RunOptions.Default with { Tasks = _directory.FullName, Template = template });

        Assert.Equal((1, $"Tasks on a copy copy EPLAN_Sample_Project.elk, which is not under C:\\Users\\Public\\EPLAN\\Data or at {template}; pass --template.\n"), (exit, output));
    }

    private static async Task<(int Exit, string Output)> Run(RunOptions options)
    {
        using var output = new StringWriter { NewLine = "\n" };
        int exit = await RunCommand.RunAsync(options, output, TestContext.Current.CancellationToken);
        return (exit, output.ToString());
    }
}
