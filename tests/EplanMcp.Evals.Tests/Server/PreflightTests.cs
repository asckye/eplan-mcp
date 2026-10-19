using EplanMcp.Evals.Server;

namespace EplanMcp.Evals.Tests.Server;

[Trait("Size", "Small")]
public sealed class PreflightTests
{
    private const string Root = @"C:\Users\engineer\EplanMcp\scratch";

    [Theory]
    [InlineData(@"C:\Users\engineer\EplanMcp\scratch\Fixture1", null)]
    [InlineData(@"c:\users\ENGINEER\eplanmcp\Scratch\run-1\Fixture1", null)]
    [InlineData(@"C:\Users\engineer\EplanMcp\scratch-old\Fixture1", @"C:\Users\engineer\EplanMcp\scratch\")]
    [InlineData(@"C:\Users\engineer\EplanMcp\scratch\..\Projects\Customer", @"C:\Users\engineer\EplanMcp\scratch\")]
    [InlineData(@"D:\Projects\Customer", @"C:\Users\engineer\EplanMcp\scratch\")]
    [InlineData("", @"C:\Users\engineer\EplanMcp\scratch\")]
    public void ScratchProblem_AcceptsOnlyProjectsInsideTheScratchRoot(string project, string? rootInMessage)
    {
        string? problem = Preflight.ScratchProblem(project, Root);

        if (rootInMessage is null)
        {
            Assert.Null(problem);
        }
        else
        {
            Assert.Equal($"the current project {project} is not under the scratch root {rootInMessage}: the evals run only on a scratch copy of the fixture", problem);
        }
    }

    [Fact]
    public async Task EplanProblem_NotConnected_PassesOnTheHint()
    {
        var client = new FakeToolClient().Answer("eplan_session_status", """{"connected":false,"hint":"Start EPLAN with the EplanMcp add-in."}""");

        Assert.Equal("EPLAN is not connected: Start EPLAN with the EplanMcp add-in.", await Problem(client));
    }

    [Fact]
    public async Task EplanProblem_NoCurrentProject_AsksToOpenTheFixture()
    {
        FakeToolClient client = Connected().Answer("eplan_project_list", """{"projects":[{"name":"Line1","current":false,"path":"C:\\Users\\engineer\\EplanMcp\\scratch\\Line1"}],"nextCursor":null}""");

        Assert.Equal("no project is current in EPLAN: open the fixture project from the scratch root", await Problem(client));
    }

    [Fact]
    public async Task EplanProblem_ACurrentProjectOnALaterPage_IsFound()
    {
        FakeToolClient client = Connected()
            .Answer("eplan_project_list", """{"projects":[{"name":"A","current":false,"path":"D:\\Projects\\A"}],"nextCursor":"c1"}""")
            .Answer("eplan_project_list", """{"projects":[{"name":"Fixture1","current":true,"path":"C:\\Users\\engineer\\EplanMcp\\scratch\\Fixture1"}],"nextCursor":null}""");

        Assert.Null(await Problem(client));
        Assert.Equal("""eplan_project_list {"limit":200,"cursor":"c1"}""", client.Calls[^1]);
    }

    [Fact]
    public async Task EplanProblem_ACustomerProject_IsRefused()
    {
        FakeToolClient client = Connected().Answer("eplan_project_list", """{"projects":[{"name":"Customer","current":true,"path":"D:\\Projects\\Customer"}]}""");

        Assert.StartsWith(@"the current project D:\Projects\Customer is not under the scratch root", await Problem(client), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EplanProblem_AFailingProjectList_IsReported()
    {
        FakeToolClient client = Connected().Fail("eplan_project_list", "EPLAN_BUSY: a dialog is open");

        Assert.Equal("eplan_project_list failed: EPLAN_BUSY: a dialog is open", await Problem(client));
    }

    private static FakeToolClient Connected() => new FakeToolClient().Answer("eplan_session_status", """{"connected":true}""");

    private static Task<string?> Problem(FakeToolClient client) => Preflight.EplanProblemAsync(client, Root, TestContext.Current.CancellationToken);
}
