using System.Text.Json.Nodes;
using EplanMcp.Workbench.Project;

namespace EplanMcp.Workbench.Tests.Project;

/// <summary>ADR-0065 decision 2: the Workbench reads with read-only tools only, and refuses any other before starting anything.</summary>
[Trait("Size", "Small")]
public sealed class HostToolClientTests
{
    [Theory]
    [InlineData("eplan_page_delete")]
    [InlineData("eplan_property_set")]
    [InlineData("eplan_api_try")]
    [InlineData("eplan_action_try")]
    public async Task AToolOffTheReadList_IsRefused_WithoutStartingTheHost(string tool)
    {
        // A Host path that does not exist: a started process would fail differently.
        await using var client = new HostToolClient(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "eplanmcp.exe"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadAsync(tool, [], CancellationToken.None));
    }

    [Theory]
    [InlineData("eplan_page_delete")]
    [InlineData("eplan_api_try")]
    [InlineData("eplan_project_get")]
    public async Task AButtonCallOffItsList_IsRefused_WithoutStartingTheHost(string tool)
    {
        await using var client = new HostToolClient(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "eplanmcp.exe"), new EplanMcp.Workbench.Approvals.EngineerWrites());

        await Assert.ThrowsAsync<ArgumentException>(() => client.RunAsync(new EngineerCall(tool, [], "h"), execute: true, CancellationToken.None));
    }

    [Fact]
    public void TheButtonsTools_AreOrdinaryWritesWithAPreview_OrExports_NeverDestructive()
    {
        Dictionary<string, JsonObject> tools = Contract();

        Assert.All(HostToolClient.WriteTools.Concat(HostToolClient.ExportTools), name =>
        {
            Assert.True(tools.ContainsKey(name), name);
            Assert.True((bool?)tools[name]["annotations"]?["destructiveHint"] == false, name);
        });
        Assert.All(HostToolClient.WriteTools, name => Assert.NotNull(tools[name]["inputSchema"]?["properties"]?["dry_run"]));
    }

    [Fact]
    public void EveryToolOnTheReadList_IsReadOnlyInTheHostsContract()
    {
        Dictionary<string, JsonObject> tools = Contract();

        Assert.All(HostToolClient.ReadTools, name =>
        {
            Assert.True(tools.ContainsKey(name), name);
            Assert.True((bool?)tools[name]["annotations"]?["readOnlyHint"] == true, name);
        });
    }

    private static Dictionary<string, JsonObject> Contract()
    {
        using Stream stream = typeof(HostToolClientTests).Assembly.GetManifestResourceStream("Snapshots/server-contract.verified.json")!;
        var contract = (JsonObject)JsonNode.Parse(stream)!;
        return (contract["tools"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(t => (string)t["name"]!);
    }
}
