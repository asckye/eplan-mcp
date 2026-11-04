using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task BusbarDefinitionWithTwoConnectionPoints_ReadsOneDevice_EachCallUndoesOnce()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project;
        string page = await NewPageAsync(project);
        // As on the sample (+A1-WE1): EPLAN keeps only the location designation of a busbar device.
        const string tag = "+A1-WE9";
        var snapshots = new List<string> { (await session.SnapshotAsync()).Hash };
        try
        {
            JsonElement definition = await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["library"] = "SPECIAL",
                ["symbol"] = "BBDEF",
                ["symbol_variant"] = 0,
                ["tag"] = "-WE9",
                ["x"] = 60,
                ["y"] = 120,
                ["dry_run"] = false
            });
            Assert.Equal(ExecutionState.Committed, definition.GetProperty("execution").GetString());
            // A86: TextFunction is the symbol type in the library; the object itself reads back as Function.
            Assert.Equal("Function", definition.GetProperty("device").GetProperty("type").GetString());
            Assert.Equal("BusBarDefText", definition.GetProperty("device").GetProperty("category").GetString());
            Assert.True(definition.GetProperty("device").GetProperty("isMainFunction").GetBoolean());
            snapshots.Add((await session.SnapshotAsync()).Hash);
            foreach (int x in new[] { 100, 140 })
            {
                JsonElement point = await PartsCallAsync("eplan_device_place", new()
                {
                    ["project"] = project,
                    ["page"] = page,
                    ["library"] = "SPECIAL",
                    ["symbol"] = "BCP",
                    ["symbol_variant"] = 0,
                    ["tag"] = "-WE9",
                    ["x"] = x,
                    ["y"] = 120,
                    ["dry_run"] = false
                });
                Assert.Equal((ExecutionState.Committed, tag), (point.GetProperty("execution").GetString(), point.GetProperty("device").GetProperty("tag").GetString()));
                Assert.False(point.GetProperty("device").GetProperty("isMainFunction").GetBoolean());
                snapshots.Add((await session.SnapshotAsync()).Hash);
            }
            JsonElement device = await PartsCallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag });
            Assert.Equal((tag, "BusBarDefText"), (device.GetProperty("device").GetProperty("tag").GetString(), device.GetProperty("device").GetProperty("category").GetString()));
            JsonElement[] points = [.. device.GetProperty("functions").EnumerateArray()];
            Assert.Equal(2, points.Length);
            Assert.All(points, p => Assert.Equal((tag, page, "Busbar"), (p.GetProperty("tag").GetString(), p.GetProperty("page").GetString(), p.GetProperty("category").GetString())));
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
            catch (Exception error) { TestContext.Current.TestOutputHelper!.WriteLine("Busbar page readback failed: " + error); }
            throw;
        }
    }
}
