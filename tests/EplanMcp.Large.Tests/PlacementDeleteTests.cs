using System.Text.Json;
namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementDeleteTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    [Fact]
    public async Task LineAndTextDeletionHasOneUndo_AndDeviceIsRefused()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = session.NextPage;
        Dictionary<string, object?> read = new() { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" };
        async Task<JsonElement> Write(string tool, Dictionary<string, object?> args)
        {
            Evidence(tool + " request", args);
            try
            {
                JsonElement result = await host.CallAsync(tool, args);
                Evidence(tool + " result", result);
                Evidence("readback", await host.CallAsync("eplan_page_get", read));
                return result;
            }
            catch (Exception error)
            {
                Evidence("failure", error.ToString());
                Evidence("failed readback", await host.CallAsync("eplan_page_get", read));
                throw;
            }
        }
        await Write("eplan_page_create", new() { ["project"] = session.Project, ["pages"] = new[] { new { name = page } }, ["dry_run"] = false });
        await Write("eplan_placement_create", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["dry_run"] = false,
            ["items"] = new object[] { new { kind = "line", points = (double[][])[[20, 40], [40, 40]] },
                new { kind = "text", position = (double[])[100, 100], contents = new Dictionary<string, string> { ["??_??"] = "delete text" }, height_mm = 3.5 } },
        });
        await Write("eplan_device_place", new() { ["project"] = session.Project, ["page"] = page, ["library"] = "IEC_symbol", ["symbol"] = "K", ["symbol_variant"] = 0, ["tag"] = "-KD" + session.NextNumber, ["x"] = 180, ["y"] = 160, ["dry_run"] = false });
        JsonElement before = await host.CallAsync("eplan_page_get", read);
        JsonElement[] rows = [.. before.GetProperty("drawings").GetProperty("items").EnumerateArray()];
        JsonElement device = Assert.Single(rows, p => p.GetProperty("kind").GetString() == "Function");
        var invalid = new Dictionary<string, object?> { ["project"] = session.Project, ["page"] = page, ["items"] = new[] { device.GetProperty("locator") }, ["dry_run"] = false };
        Evidence("device deletion request", invalid);
        JsonElement refused = await host.ErrorAsync("eplan_placement_delete", invalid);
        Evidence("device deletion result", refused);
        Assert.Equal("INVALID_ARGUMENT", refused.GetProperty("reason").GetString());
        Assert.Equal("not_started", refused.GetProperty("execution").GetString());
        JsonElement[] targets = [.. rows.Where(p => p.GetProperty("kind").GetString() is "Line" or "Text").Select(p => p.GetProperty("locator"))];
        Assert.Equal(2, targets.Length);
        Dictionary<string, object?> delete = new() { ["project"] = session.Project, ["page"] = page, ["items"] = targets };
        JsonElement preview = await host.CallAsync("eplan_placement_delete", delete);
        Assert.Equal("not_started", preview.GetProperty("execution").GetString());
        host.Approvals.Approve("eplan_placement_delete", session.Project, targets.Select(l => $"delete drawing {page}:{l.GetProperty("type")}/{l.GetProperty("oid")}"));
        string state = await session.StateAsync();
        delete["dry_run"] = false;
        JsonElement result = await Write("eplan_placement_delete", delete);
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        JsonElement after = await host.CallAsync("eplan_page_get", read);
        Assert.DoesNotContain(after.GetProperty("drawings").GetProperty("items").EnumerateArray(), p => p.GetProperty("kind").GetString() is "Line" or "Text");
        await session.UndoAsync();
        Assert.Equal(state, await session.StateAsync());
        JsonElement restored = await host.CallAsync("eplan_page_get", read);
        Evidence("undo readback", restored);
        Assert.Equal(before.GetProperty("drawings").GetProperty("items").GetArrayLength(), restored.GetProperty("drawings").GetProperty("items").GetArrayLength());
    }
    private static void Evidence(string label, object value) => TestContext.Current.TestOutputHelper!.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}
