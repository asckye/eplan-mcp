using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task BlackBoxWithThreeConnectionPoints_ReadsBoundsNamesAndMembership_EachCallUndoesOnce()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project;
        string page = await NewPageAsync(project);
        const string tag = "=GAA+A1-PF9";
        var snapshots = new List<string> { (await session.SnapshotAsync()).Hash };
        var names = new List<string>();
        try
        {
            JsonElement box = await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["library"] = "SPECIAL",
                ["symbol"] = "DC",
                ["symbol_variant"] = 0,
                ["tag"] = "-PF9",
                ["x"] = 60,
                ["y"] = 120,
                ["corner_x"] = 120,
                ["corner_y"] = 160,
                ["dry_run"] = false
            });
            Assert.Equal(ExecutionState.Committed, box.GetProperty("execution").GetString());
            Assert.Equal("BoxedDevice", box.GetProperty("device").GetProperty("type").GetString());
            Assert.Equal("Blackbox", box.GetProperty("device").GetProperty("category").GetString());
            Assert.True(box.GetProperty("device").GetProperty("isMainFunction").GetBoolean());
            Assert.Equal(tag, box.GetProperty("blackBox").GetProperty("tag").GetString());
            Assert.Equal([60d, 120d, 120d, 160d], box.GetProperty("blackBox").GetProperty("bounds").EnumerateArray().Select(n => n.GetDouble()));
            snapshots.Add((await session.SnapshotAsync()).Hash);
            foreach ((int variant, int number, int x) in new[] { (0, 1, 70), (2, 2, 90), (4, 3, 110) })
            {
                JsonElement point = await PartsCallAsync("eplan_device_place", new()
                {
                    ["project"] = project,
                    ["page"] = page,
                    ["library"] = "SPECIAL",
                    ["symbol"] = "DCP",
                    ["symbol_variant"] = variant,
                    ["tag"] = "-PF9",
                    ["x"] = x,
                    ["y"] = 140,
                    ["dry_run"] = false
                });
                names.Add(tag + ":" + number);
                Assert.Equal((ExecutionState.Committed, names[^1]), (point.GetProperty("execution").GetString(), point.GetProperty("device").GetProperty("tag").GetString()));
                Assert.False(point.GetProperty("device").GetProperty("isMainFunction").GetBoolean());
                JsonElement owner = point.GetProperty("blackBox");
                Assert.Equal(tag, owner.GetProperty("tag").GetString());
                Assert.Equal(names.Order(StringComparer.Ordinal), owner.GetProperty("functionsInside").EnumerateArray().Select(n => n.GetString()).Order(StringComparer.Ordinal));
                snapshots.Add((await session.SnapshotAsync()).Hash);
            }
            JsonElement device = await PartsCallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag });
            Assert.Equal(tag, device.GetProperty("device").GetProperty("tag").GetString());
            Assert.Equal(names.Order(StringComparer.Ordinal), device.GetProperty("functions").EnumerateArray().Select(f => f.GetProperty("tag").GetString()).Order(StringComparer.Ordinal));
            JsonElement drawings = await PartsCallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200 });
            Assert.Single(drawings.GetProperty("drawings").GetProperty("items").EnumerateArray(), p => p.GetProperty("kind").GetString() == "BoxedDevice");
            for (int i = snapshots.Count - 2; i >= 0; i--)
            {
                await session.UndoAsync();
                Assert.Equal(snapshots[i], (await session.SnapshotAsync()).Hash);
            }
        }
        catch
        {
            await PartsFailureReadbackAsync(project, tag);
            try
            { await PartsCallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200 }); }
            catch (Exception error) { TestContext.Current.TestOutputHelper!.WriteLine("Black-box page readback failed: " + error); }
            throw;
        }
    }
}
