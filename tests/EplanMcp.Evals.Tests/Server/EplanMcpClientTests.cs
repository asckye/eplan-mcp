using System.Text.Json.Nodes;
using EplanMcp.Evals.Server;

namespace EplanMcp.Evals.Tests.Server;

/// <summary>
/// One real eplanmcp over stdio for the tests of <see cref="EplanMcpClientTests"/>, without EPLAN. A pipe prefix of its
/// own keeps it away from an EPLAN that may be running on the machine (EPLANMCP_PIPE_PREFIX, as in the Host's
/// end-to-end tests). Shared, because closing the client waits for eplanmcp to exit, about five seconds.
/// </summary>
public sealed class EplanMcpServerFixture : IAsyncLifetime
{
    internal EplanMcpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var environment = new Dictionary<string, string?> { ["EPLANMCP_PIPE_PREFIX"] = "EplanMcpEvals." + Guid.NewGuid().ToString("N") + "." };
        Client = await EplanMcpClient.StartAsync(Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"), environment, TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => Client.DisposeAsync();
}

[Trait("Size", "Medium")]
public sealed class EplanMcpClientTests(EplanMcpServerFixture server) : IClassFixture<EplanMcpServerFixture>
{
    [Fact]
    public async Task ListTools_HasTheReadTools()
    {
        IReadOnlyList<string> tools = await server.Client.ListToolsAsync(TestContext.Current.CancellationToken);

        Assert.Contains("eplan_session_status", tools);
        Assert.Contains("eplan_project_list", tools);
        Assert.All(tools, t => Assert.StartsWith("eplan_", t, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Call_WithoutEplan_GivesTheStructuredStatusAndTheErrorText()
    {
        ToolOutcome status = await server.Client.CallAsync("eplan_session_status", [], TestContext.Current.CancellationToken);
        ToolOutcome projects = await server.Client.CallAsync("eplan_project_list", new JsonObject { ["limit"] = 10 }, TestContext.Current.CancellationToken);

        Assert.False(status.IsError);
        Assert.Equal("false", status.Structured!["connected"]!.ToJsonString());
        Assert.True(projects.IsError);
        Assert.Contains("EPLAN_NOT_CONNECTED", projects.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EplanProblem_WithoutEplan_SaysSoAndHowToFixIt()
    {
        string? problem = await Preflight.EplanProblemAsync(server.Client, Preflight.ScratchRoot(), TestContext.Current.CancellationToken);

        Assert.StartsWith("EPLAN is not connected: ", problem, StringComparison.Ordinal);
        Assert.True(problem!.Length > "EPLAN is not connected: ".Length);
    }
}
