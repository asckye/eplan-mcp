using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task BusbarMain_SecondPartWithCountTwo_ReadsQuantity_OneUndoRestores()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project, page = await NewPageAsync(project), tag = "";
        try
        {
            JsonElement placed = await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["library"] = "SPECIAL",
                ["symbol"] = "BBDEF",
                ["symbol_variant"] = 0,
                ["tag"] = "-WECOUNT" + page[Prefix.Length..],
                ["x"] = 60,
                ["y"] = 120,
                ["dry_run"] = false,
            });
            tag = placed.GetProperty("device").GetProperty("tag").GetString()!;
            await PartsCallAsync("eplan_device_update", new() { ["project"] = project, ["tag"] = tag, ["part"] = "RIT.3581000", ["dry_run"] = false });
            string before = (await session.SnapshotAsync()).Hash;
            JsonElement original = await PartsCallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag });
            JsonElement result = await PartsCallAsync("eplan_device_update", new()
            { ["project"] = project, ["tag"] = tag, ["part"] = "RIT.9340030", ["variant"] = "1", ["count"] = 2, ["dry_run"] = false });
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            JsonElement read = await PartsCallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag });
            JsonElement part = Assert.Single(read.GetProperty("parts").EnumerateArray(), p => p.GetProperty("partNumber").GetString() == "RIT.9340030");
            Assert.Equal(("1", 2), (part.GetProperty("variant").GetString(), part.GetProperty("count").GetInt32()));
            Assert.Equal(1, Assert.Single(read.GetProperty("parts").EnumerateArray(), p => p.GetProperty("partNumber").GetString() == "RIT.3581000").GetProperty("count").GetInt32());
            await session.UndoAsync();
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
            Assert.Equal(original.GetRawText(), (await PartsCallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag })).GetRawText());
        }
        catch { await PartsFailureReadbackAsync(project, tag); throw; }
    }
}
