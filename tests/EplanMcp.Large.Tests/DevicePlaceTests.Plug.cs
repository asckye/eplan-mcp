using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Theory]
    [InlineData("PDEF", "ConnectorDefText", 0)]
    [InlineData("XBS", "Plug", 2)]
    public async Task PlugDefinitionOrPin_ReadsCategoryAndPins_OneUndoRestores(string symbol, string category, int pinCount)
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project;
        string page = await NewPageAsync(project);
        string tag = "-XP" + page[Prefix.Length..];
        LiveSnapshots.Snapshot before = await session.SnapshotAsync();
        var args = new Dictionary<string, object?>
        {
            ["project"] = project,
            ["page"] = page,
            ["library"] = symbol == "PDEF" ? "SPECIAL" : "IEC_symbol",
            ["symbol"] = symbol,
            ["symbol_variant"] = 0,
            ["tag"] = tag,
            ["x"] = 80,
            ["y"] = 160,
            ["dry_run"] = false
        };
        if (pinCount > 0)
        { args["connection_designations"] = new[] { "1", "2" }; }
        JsonElement placed = await PartsCallAsync("eplan_device_place", args);
        Assert.Equal((ExecutionState.Committed, "AI: eplan_device_place"), (placed.GetProperty("execution").GetString(), placed.GetProperty("undo").GetString()));
        Assert.Equal(category, placed.GetProperty("symbol").GetProperty("functionCategory").GetString());
        Assert.Equal(category, placed.GetProperty("device").GetProperty("category").GetString());
        string actualTag = placed.GetProperty("device").GetProperty("tag").GetString()!;
        JsonElement read = await PartsCallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = actualTag });
        JsonElement device = read.GetProperty("device");
        Assert.Equal((actualTag, page, category), (device.GetProperty("tag").GetString(), device.GetProperty("page").GetString(), device.GetProperty("category").GetString()));
        Assert.Equal(pinCount, device.GetProperty("pins").GetArrayLength());
        if (pinCount > 0)
        { Assert.Equal(["1", "2"], device.GetProperty("pins").EnumerateArray().Select(pin => pin.GetString())); }
        Assert.Equal(1, await DevicesOnAsync(project, page));
        await session.UndoAsync();
        Assert.Equal(0, await DevicesOnAsync(project, page));
        Assert.Equal(before.Hash, (await session.SnapshotAsync()).Hash);
    }
}
