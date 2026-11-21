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

    [Fact]
    public void EveryToolOnTheReadList_IsReadOnlyInTheHostsContract()
    {
        using Stream stream = typeof(HostToolClientTests).Assembly.GetManifestResourceStream("Snapshots/server-contract.verified.json")!;
        var contract = (JsonObject)JsonNode.Parse(stream)!;
        Dictionary<string, JsonObject> tools = (contract["tools"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(t => (string)t["name"]!);

        Assert.All(HostToolClient.ReadTools, name =>
        {
            Assert.True(tools.ContainsKey(name), name);
            Assert.True((bool?)tools[name]["annotations"]?["readOnlyHint"] == true, name);
        });
    }
}
