using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PageDrawingDiscoveryTests(LiveEplan eplan)
{
    [Fact]
    public async Task FixtureCircuit_DiscoveryIsCompleteAndChangesNoEngineeringData()
    {
        const string page = "=GAA+A1&EFS1/1";
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(eplan.ProjectPath);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        var scope = new ProjectSnapshotArgs { Project = eplan.ProjectPath, Pages = [page] };
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, scope);
        PageDrawingGeometry expected = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = eplan.ProjectPath, Page = page }, TestContext.Current.CancellationToken);
        Assert.NotEmpty(expected.Placements);
        var locators = new List<(int Type, long Oid, string Digest)>();
        string? cursor = null;
        int calls = 0;
        do
        {
            JsonElement data = await eplan.CallAsync("eplan_page_get", new()
            {
                ["project"] = eplan.ProjectPath,
                ["page"] = page,
                ["include_drawings"] = true,
                ["drawing_limit"] = 2,
                ["drawing_cursor"] = cursor,
            });
            Assert.False(data.TryGetProperty("execution", out _));
            JsonElement drawings = data.GetProperty("drawings");
            Assert.Equal(expected.GridSize, drawings.GetProperty("gridSize").GetDouble());
            Assert.Equal(expected.PageSize is null ? "unknown" : "Page.Size", drawings.GetProperty("boundarySource").GetString());
            Assert.Equal(expected.Placements.Count, drawings.GetProperty("total").GetInt32());
            foreach (JsonElement row in drawings.GetProperty("items").EnumerateArray())
            {
                JsonElement locator = row.GetProperty("locator");
                locators.Add((locator.GetProperty("type").GetInt32(), locator.GetProperty("oid").GetInt64(), locator.GetProperty("digest").GetString()!));
                Assert.Equal(2, row.GetProperty("location").GetArrayLength());
                Assert.True(row.TryGetProperty("properties", out _));
            }
            cursor = drawings.GetProperty("nextCursor").GetString();
            Assert.Equal(cursor is not null, drawings.GetProperty("truncated").GetBoolean());
            Assert.True(++calls <= expected.Placements.Count);
        }
        while (cursor is not null);
        Assert.Equal(expected.Placements.Select(p => (p.Locator.Type, p.Locator.Oid, p.Locator.Digest)), locators);
        Assert.Equal(locators.Count, locators.Distinct().Count());
        LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(bridge, scope);
        Assert.Equal(before.Hash, after.Hash);
        Assert.Equal(files, LiveSnapshots.DesignFiles(eplan.ProjectPath));
    }
}
