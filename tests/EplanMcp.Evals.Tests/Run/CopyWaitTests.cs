using EplanMcp.Evals.Run;
using EplanMcp.Evals.Tests.Server;

namespace EplanMcp.Evals.Tests.Run;

[Trait("Size", "Small")]
public sealed class CopyWaitTests
{
    private const string FixturePath = @"C:\s\fixtures\EPLAN_Sample_Project";
    private const string Fixture = """{"name":"EPLAN_Sample_Project","path":"C:\\s\\fixtures\\EPLAN_Sample_Project","current":CURRENT}""";
    private const string First = """{"name":"Eval_r1_01","path":"C:\\s\\evals\\r1\\copies\\01\\Eval_r1_01","current":CURRENT}""";
    private const string Other = """{"name":"Line1","path":"C:\\s\\Line1","current":CURRENT}""";

    [Fact]
    public async Task WaitUntilOpen_WaitsForEveryCopyAndForTheFixtureToBeCurrentAgain()
    {
        FakeToolClient client = new FakeToolClient()
            .Answer("eplan_project_list", List(Current(Fixture)))
            .Answer("eplan_project_list", List(NotCurrent(Fixture), Current(First)))
            .Answer("eplan_project_list", List(Current(Fixture), NotCurrent(First).Replace("C:\\\\s", "c:\\\\S", StringComparison.Ordinal)));
        Dictionary<(string Task, int Attempt), AttemptCopy> copies = Copies();
        using var progress = new StringWriter { NewLine = "\n" };

        bool fixtureCurrent = await ProjectCopies.WaitUntilOpenAsync(client, copies, FixturePath, progress, TimeSpan.FromMinutes(1), TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.True(fixtureCurrent);
        Assert.Null(copies[("write", 1)].Problem);
        Assert.Equal(3, client.Calls.Count);
        Assert.Equal(
            "0 of 1 copies are open in EPLAN\n"
            + "1 of 1 copies are open in EPLAN; the fixture is not the current project: select EPLAN_Sample_Project in the page navigator again\n"
            + "1 of 1 copies are open in EPLAN\n",
            progress.ToString());
    }

    [Fact]
    public async Task WaitUntilOpen_ACopyNotOpenedInTime_GetsAProblemAndOneThatFailedBeforeIsNotAwaited()
    {
        FakeToolClient client = new FakeToolClient().Answer("eplan_project_list", List(Current(Fixture)));
        Dictionary<(string Task, int Attempt), AttemptCopy> copies = Copies();
        copies[("write", 2)] = new("Eval_r1_02", @"C:\s\evals\r1\copies\02\Eval_r1_02", "copying the sample project failed: disk full");

        Assert.True(await ProjectCopies.WaitUntilOpenAsync(client, copies, FixturePath, TextWriter.Null, TimeSpan.Zero, TimeSpan.Zero, TestContext.Current.CancellationToken));

        Assert.Equal("it was not opened in EPLAN within 0 minutes", copies[("write", 1)].Problem);
        Assert.Equal("copying the sample project failed: disk full", copies[("write", 2)].Problem);
    }

    [Fact]
    public async Task WaitUntilOpen_AnotherProjectStillCurrentAtTheEnd_SaysSo()
    {
        FakeToolClient client = new FakeToolClient().Answer("eplan_project_list", List(NotCurrent(Fixture), NotCurrent(First), Current(Other)));
        Dictionary<(string Task, int Attempt), AttemptCopy> copies = Copies();

        Assert.False(await ProjectCopies.WaitUntilOpenAsync(client, copies, FixturePath, TextWriter.Null, TimeSpan.Zero, TimeSpan.Zero, TestContext.Current.CancellationToken));
        Assert.Null(copies[("write", 1)].Problem);
    }

    [Fact]
    public async Task CurrentProject_IsThePathOfTheCurrentOne()
    {
        Assert.Equal(FixturePath, await ProjectCopies.CurrentProjectAsync(new FakeToolClient().Answer("eplan_project_list", List(NotCurrent(Other), Current(Fixture))), TestContext.Current.CancellationToken));
        Assert.Null(await ProjectCopies.CurrentProjectAsync(new FakeToolClient().Answer("eplan_project_list", List(NotCurrent(Other))), TestContext.Current.CancellationToken));
    }

    private static Dictionary<(string Task, int Attempt), AttemptCopy> Copies() =>
        new() { [("write", 1)] = new("Eval_r1_01", @"C:\s\evals\r1\copies\01\Eval_r1_01") };

    private static string Current(string project) => project.Replace("CURRENT", "true", StringComparison.Ordinal);

    private static string NotCurrent(string project) => project.Replace("CURRENT", "false", StringComparison.Ordinal);

    private static string List(params string[] projects) => $$"""{"projects":[{{string.Join(",", projects)}}],"nextCursor":null}""";
}
