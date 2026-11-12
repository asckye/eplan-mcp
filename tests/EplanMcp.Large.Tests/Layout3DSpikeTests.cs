using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// M4b spike (2026-11-11, Pro Panel): prints what the 3D API does on the scratch LargeWrite copy, inside one transaction
/// that is rolled back, so the tools are designed from observed facts. Nothing is asserted beyond the run completing and
/// the rollback leaving the inventory as before.
/// </summary>
[Trait("Size", "Large"), Trait("Spike", "Layout3D")]
public sealed class Layout3DSpikeTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    private static readonly string[] _spaces = ["A1", "A9"];
    private static readonly object[] _doorDevices = [new { part = "SE.XB4BS844415", x = 100, y = 300 }, new { part = "SE.XB4BW31B5", x = 160, y = 300 }];

    [Fact]
    public async Task EnclosureRailDuctAndDevices_PrintTreeMatesAndPlacement_RollbackLeavesNothing()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper));
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            JsonElement database = await host.CallAsync("eplan_part_search", new() { ["query"] = "RIT.", ["limit"] = 50 });
            TestContext.Current.TestOutputHelper!.WriteLine("parts database: " + database);
            // Two devices with parts, for the device route (existing unplaced 3D representation, else a component by part).
            JsonElement devices = await host.CallAsync("eplan_device_list", new() { ["project"] = host.Writable, ["limit"] = 200 });
            var picked = new List<object>();
            foreach (JsonElement device in devices.GetProperty("devices").EnumerateArray())
            {
                string tag = device.GetProperty("tag").GetString()!;
                JsonElement detail = await host.CallAsync("eplan_device_get", new() { ["project"] = host.Writable, ["tag"] = tag });
                if (detail.TryGetProperty("parts", out JsonElement parts) && parts.GetArrayLength() > 0)
                {
                    picked.Add(new { tag, part = parts[0].GetProperty("partNumber").GetString(), x = 40 + 60 * picked.Count });
                }
                if (picked.Count == 2)
                { break; }
            }
            TestContext.Current.TestOutputHelper!.WriteLine("devices: " + JsonSerializer.Serialize(picked));
            JsonElement read = await Probe(new { project = session.Project, operation = "read" });
            Assert.True(read.GetProperty("success").GetBoolean(), read.ToString());
            JsonElement created = await Probe(new
            {
                project = session.Project,
                operation = "create",
                keep = true,
                space = "A9",
                location = "A9",
                doorDevices = _doorDevices,
                modelView = true,
                cabinetPart = "RIT.1055500",
                railPart = "PXC.0806602",
                railLength = 500,
                ductPart = "PXC.3240192",
                ductLength = 500,
                devices = picked,
            });
            Assert.True(created.GetProperty("success").GetBoolean(), created.ToString());
            // Committed (scratch copy): a separate call so no transaction is open, first an existing space, then the new one.
            JsonElement views = await Probe(new { project = session.Project, operation = "modelview", spaces = _spaces });
            Assert.True(views.GetProperty("success").GetBoolean(), views.ToString());
            // The same action from outside (eplremote), as the action census runs actions, on the committed pages.
            foreach (JsonElement step in views.GetProperty("steps").EnumerateArray().Where(s => s.TryGetProperty("page", out _)))
            {
                string page = step.GetProperty("page").GetString()!;
                foreach (string layout in new[] { "A1", "+A1" })
                {
                    string action = "InsertModelViewAction /PROJECTNAME:\"" + session.Project + ".elk\" /PAGENAME:\"" + page + "\" /LAYOUTSPACE:" + layout
                        + " /VIEWNAME:Spike /X:20 /Y:20 /DX:300 /DY:200 /STYLE:1 /VIEWPOINT:5 /SCALESETTING:0 /VIEWTYPE:1";
                    string reply = "ok";
                    try
                    { await PlanStepLiveSession.ActionAsync(action); }
                    catch (Exception error) when (error is not OperationCanceledException)
                    { reply = "failed: " + error.Message; }
                    JsonElement drawn = await host.CallAsync("eplan_page_get", new() { ["project"] = host.Writable, ["page"] = page, ["include_drawings"] = true });
                    TestContext.Current.TestOutputHelper!.WriteLine($"external {page} {layout}: {reply} | drawings {drawn.GetProperty("drawings").GetProperty("total")}");
                }
            }
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }

    private async Task<JsonElement> Probe(object request)
    {
        string file = Path.Combine(host.Home, "layout3d-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(request), TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargeLayout3DSpike /REQUEST:\"" + file + "\"");
        string observed = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine(observed);
        return JsonDocument.Parse(observed).RootElement.Clone();
    }
}
