using EplanMcp.Evals.Run;
using EplanMcp.Evals.Tests.Server;

namespace EplanMcp.Evals.Tests.Run;

[Trait("Size", "Small")]
public sealed class CopyWaitTests
{
    private const string Fixture = """{"name":"EPLAN_Sample_Project","path":"C:\\s\\fixtures\\EPLAN_Sample_Project","current":CURRENT}""";
    private const string First = """{"name":"Eval_r1_01","path":"C:\\s\\evals\\r1\\copies\\01\\Eval_r1_01","current":CURRENT}""";

    [Fact]
    public async Task WaitUntilOpen_WaitsForEveryCopyAndForTheFixtureToBeCurrentAgain()
    {
        FakeToolClient client = new FakeToolClient()
            .Answer("eplan_project_list", List(Fixture.Replace("CURRENT", "true", StringComparison.Ordinal)))
            .Answer("eplan_project_list", List(Fixture.Replace("CURRENT", "false", StringComparison.Ordinal), First.Replace("CURRENT", "true", StringComparison.Ordinal)))
            .Answer("eplan_project_list", List(Fixture.Replace("CURRENT", "true", StringComparison.Ordinal), First.Replace("CURRENT", "false", StringComparison.Ordinal).Replace("C:\\\\s", "c:\\\\S", StringComparison.Ordinal)));
        using var progress = new StringWriter { NewLine = "\n" };

        Dictionary<(string Task, int Attempt), AttemptCopy> copies = await ProjectCopies.WaitUntilOpenAsync(client, Copies(), progress, TimeSpan.FromMinutes(1), TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Null(copies[("write", 1)].Problem);
        Assert.Equal(3, client.Calls.Count);
        Assert.Equal(
            "0 of 1 copies are open in EPLAN\n"
            + "1 of 1 copies are open in EPLAN; a copy is the current project: select the fixture in the page navigator again\n"
            + "1 of 1 copies are open in EPLAN\n",
            progress.ToString());
    }

    [Fact]
    public async Task WaitUntilOpen_ACopyNotOpenedInTime_GetsAProblemAndOneThatFailedBeforeIsNotAwaited()
    {
        FakeToolClient client = new FakeToolClient().Answer("eplan_project_list", List(Fixture.Replace("CURRENT", "true", StringComparison.Ordinal)));
        Dictionary<(string Task, int Attempt), AttemptCopy> copies = Copies();
        copies[("write", 2)] = new("Eval_r1_02", @"C:\s\evals\r1\copies\02\Eval_r1_02", "copying the sample project failed: disk full");

        copies = await ProjectCopies.WaitUntilOpenAsync(client, copies, TextWriter.Null, TimeSpan.Zero, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal("it was not opened in EPLAN within 0 minutes", copies[("write", 1)].Problem);
        Assert.Equal("copying the sample project failed: disk full", copies[("write", 2)].Problem);
    }

    private static Dictionary<(string Task, int Attempt), AttemptCopy> Copies() =>
        new() { [("write", 1)] = new("Eval_r1_01", @"C:\s\evals\r1\copies\01\Eval_r1_01") };

    private static string List(params string[] projects) => $$"""{"projects":[{{string.Join(",", projects)}}],"nextCursor":null}""";
}
