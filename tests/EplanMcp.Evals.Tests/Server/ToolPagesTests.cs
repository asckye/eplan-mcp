using System.Text.Json.Nodes;
using EplanMcp.Evals.Server;

namespace EplanMcp.Evals.Tests.Server;

[Trait("Size", "Small")]
public sealed class ToolPagesTests
{
    [Fact]
    public async Task All_AResultWithoutACursor_IsOnePage()
    {
        PagedOutcome outcome = await All(new FakeToolClient().Answer("eplan_project_get", """{"name":"Line1"}"""), "eplan_project_get");

        Assert.Null(outcome.Problem);
        Assert.Single(outcome.Pages);
    }

    [Fact]
    public async Task All_TheSameCursorAgain_StopsInsteadOfLooping()
    {
        FakeToolClient client = new FakeToolClient()
            .Answer("eplan_page_list", """{"pages":[],"nextCursor":"c0","truncated":true}""")
            .Answer("eplan_page_list", """{"pages":[],"nextCursor":"c0","truncated":true}""");

        PagedOutcome outcome = await All(client, "eplan_page_list");

        Assert.Equal("eplan_page_list cannot return its next entry within the size budget; ask for less detail", outcome.Problem);
        Assert.Equal(2, client.Calls.Count);
    }

    [Fact]
    public async Task All_MoreThanTheMaximumOfPages_Stops()
    {
        var client = new FakeToolClient();
        for (int page = 1; page <= ToolPages.MaxPages + 1; page++)
        {
            client.Answer("eplan_page_list", $$"""{"pages":[{"name":"/{{page}}"}],"nextCursor":"c{{page}}"}""");
        }

        PagedOutcome outcome = await All(client, "eplan_page_list");

        Assert.Equal("eplan_page_list has more than 100 pages; narrow the arguments", outcome.Problem);
        Assert.Equal(ToolPages.MaxPages, client.Calls.Count);
    }

    [Fact]
    public async Task All_AResultWithoutStructuredContent_IsAProblem()
    {
        PagedOutcome outcome = await All(new FakeToolClient().Unstructured("eplan_tool_usage", "text only"), "eplan_tool_usage");

        Assert.Equal("eplan_tool_usage returned no structured result", outcome.Problem);
    }

    private static Task<PagedOutcome> All(FakeToolClient client, string tool) =>
        ToolPages.AllAsync(client, tool, new JsonObject(), TestContext.Current.CancellationToken);
}
