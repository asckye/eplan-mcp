using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task BcpWithPart_AfterBusbarDefinition_ReadsOwnReference_OneUndoRestores()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project, page = await NewPageAsync(project);
        const string tag = "+A1-WE1";
        await RegisterPartReadbackAsync();
        try
        {
            await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["library"] = "SPECIAL",
                ["symbol"] = "BBDEF",
                ["symbol_variant"] = 0,
                ["tag"] = tag,
                ["x"] = 60,
                ["y"] = 120,
                ["dry_run"] = false
            });
            string before = (await session.SnapshotAsync()).Hash;
            JsonElement result = await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["library"] = "SPECIAL",
                ["symbol"] = "BCP",
                ["symbol_variant"] = 2,
                ["tag"] = tag,
                ["join_device"] = true,
                ["part"] = "RIT.3451500",
                ["variant"] = "1",
                ["x"] = 100,
                ["y"] = 120,
                ["dry_run"] = false
            });
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            JsonElement read = await ReadPlacedPartAsync(project, page, tag, "Busbar", 100, 120);
            Assert.False(read.GetProperty("main").GetBoolean());
            JsonElement part = Assert.Single(read.GetProperty("parts").EnumerateArray());
            Assert.Equal(("RIT.3451500", "1", 1),
                (part.GetProperty("part").GetString(), part.GetProperty("variant").GetString(), part.GetProperty("count").GetInt32()));
            await session.UndoAsync();
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
        }
        catch
        {
            await PartsFailureReadbackAsync(project, tag);
            try
            { await ReadPlacedPartAsync(project, page, tag, "Busbar", 100, 120); }
            catch (Exception error) { TestContext.Current.TestOutputHelper!.WriteLine("BCP readback failed: " + error); }
            throw;
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }
}
