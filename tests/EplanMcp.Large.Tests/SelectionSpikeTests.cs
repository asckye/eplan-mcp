using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>Native UI evidence and exact product selection on new scratch pages.</summary>
[Trait("Size", "Large")]
public sealed class SelectionSpikeTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task NativeNoEditorDifferentPagesAndPropertyPlacement_PrintReadbackAndPreserveOneUndo()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string[] pages = await session.CreatePagesAsync(2);
        await host.CallAsync("eplan_device_place", new()
        {
            ["project"] = session.Project,
            ["page"] = pages[0],
            ["library"] = "IEC_symbol",
            ["symbol"] = "K",
            ["symbol_variant"] = 0,
            ["x"] = 80,
            ["y"] = 160,
            ["tag"] = "-KS" + Guid.NewGuid().ToString("N")[..6],
            ["display_properties"] = new[] { new { property_id = 20214, offset_x_mm = 8, offset_y_mm = 8 } }
        });
        await host.CallAsync("eplan_placement_create", new()
        {
            ["project"] = session.Project,
            ["page"] = pages[1],
            ["snap_to_grid"] = false,
            ["items"] = new[] { new { kind = "line", points = (int[][])[[40, 40], [80, 40]] } }
        });
        await InitializeEditorsAsync(session, pages);
        string before = (await session.SnapshotAsync()).Hash;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string dll = Path.Combine(root, "spikes", "S45-selection", "bin", "Debug", "net481", "EplanMcp.Spikes.S45.dll");
        Assert.True(File.Exists(dll), "Build spikes/S45-selection first; do not run EPLAN as part of building.");
        string folder = Directory.CreateDirectory(Path.Combine(root, ".local", "spikes", "s42")).FullName;
        string log = Path.Combine(folder, "s45-" + Guid.NewGuid().ToString("N") + ".jsonl");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + dll + "\"");
        await PlanStepLiveSession.ActionAsync("EplanMcpSelectionSpike /PageA:\"" + pages[0] + "\" /PageB:\"" + pages[1] + "\" /Log:\"" + log + "\"");
        var clock = Stopwatch.StartNew();
        string[] lines = [];
        while (clock.Elapsed < TimeSpan.FromSeconds(65))
        {
            if (File.Exists(log))
            {
                lines = File.ReadAllLines(log);
                if (lines.Any(l => l.Contains("\"phase\":\"completed\"", StringComparison.Ordinal) || l.Contains("\"phase\":\"failure\"", StringComparison.Ordinal)))
                { break; }
            }
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        foreach (string line in lines)
        { TestContext.Current.TestOutputHelper!.WriteLine(line); }
        JsonElement[] observations = [.. lines.Select(l => JsonDocument.Parse(l).RootElement)];
        Assert.Contains(observations, o => o.GetProperty("phase").GetString() == "completed");
        Assert.DoesNotContain(observations, o => o.GetProperty("phase").GetString() == "failure");
        Assert.Equal(before, (await session.SnapshotAsync()).Hash);
        foreach (string phase in new[] { "no_editor", "one_page", "different_pages", "property_placement", "focused_one_page" })
        {
            JsonElement data = Assert.Single(observations, o => o.GetProperty("phase").GetString() == phase).GetProperty("data");
            Assert.Equal(data.GetProperty("designBefore").GetRawText(), data.GetProperty("designAfter").GetRawText());
        }
        JsonElement focused = Assert.Single(observations, o => o.GetProperty("phase").GetString() == "focused_one_page").GetProperty("data");
        Assert.Equal(focused.GetProperty("requested")[0].GetProperty("id").GetString(), Assert.Single(focused.GetProperty("actual").EnumerateArray()).GetProperty("id").GetString());
        JsonElement property = Assert.Single(observations, o => o.GetProperty("phase").GetString() == "property_identity").GetProperty("data");
        Assert.Equal(property.GetProperty("parent").GetString(), property.GetProperty("property").GetString());
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        PageDrawingGeometry drawing = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, new() { Project = session.Project, Page = pages[0] }, TestContext.Current.CancellationToken);
        DrawingLocator propertyLocator = drawing.Placements.First(p => p.Kind == "PropertyPlacement").Locator;
        JsonElement refused = await host.ErrorAsync("eplan_selection_set", new() { ["locators"] = new[] { propertyLocator }, ["show_page"] = true });
        Assert.Equal(ErrorReason.PlanStale, refused.GetProperty("reason").GetString());
        await session.UndoAsync();
        JsonElement after = await host.CallAsync("eplan_object_find", new() { ["project"] = session.Project, ["page"] = pages[1], ["object_type"] = "Line" });
        Assert.Empty(after.GetProperty("items").EnumerateArray());
        Assert.NotEmpty((await host.CallAsync("eplan_object_find", new() { ["project"] = session.Project, ["page"] = pages[0], ["object_type"] = "Function" })).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ProductSelection_ExactDigestPagingAddClear_LeavesDesignAndUndoUnchanged()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        string baseline = (await session.SnapshotAsync()).Hash;
        await host.CallAsync("eplan_placement_create", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["snap_to_grid"] = false,
            ["items"] = new[] { new { kind = "rectangle", corners = (int[][])[[40, 40], [80, 80]] }, new { kind = "rectangle", corners = (int[][])[[100, 40], [140, 80]] } }
        });
        JsonElement[] locators = [.. (await host.CallAsync("eplan_object_find", new() { ["project"] = session.Project, ["page"] = page, ["object_type"] = "Rectangle" })).GetProperty("items").EnumerateArray().Select(o => o.GetProperty("locator"))];
        Assert.Equal(2, locators.Length);
        await InitializeEditorsAsync(session, [page], async _ =>
        {
            // Keep the original failing request before any page_open has initialized this editor.
            JsonElement initial = await host.CallAsync("eplan_selection_set", new() { ["locators"] = new[] { locators[0] }, ["show_page"] = true });
            Assert.True(initial.GetProperty("exact").GetBoolean(), initial.GetRawText());
            Assert.Empty(initial.GetProperty("unselected").EnumerateArray());
        });
        var before = LiveSnapshots.DesignFiles(session.Project);
        for (int i = 0; i < 2; i++)
        {
            JsonElement set = await host.CallAsync("eplan_selection_set", new() { ["locators"] = new[] { locators[0] }, ["show_page"] = true });
            Assert.True(set.GetProperty("exact").GetBoolean());
            Assert.Empty(set.GetProperty("unselected").EnumerateArray());
            Assert.True((await host.CallAsync("eplan_selection_set", new() { ["mode"] = "add", ["locators"] = new[] { locators[1] } })).GetProperty("exact").GetBoolean());
            JsonElement first = await host.CallAsync("eplan_selection_get", new() { ["limit"] = 1 });
            JsonElement second = await host.CallAsync("eplan_selection_get", new() { ["limit"] = 1, ["cursor"] = first.GetProperty("cursor").GetString() });
            Assert.Equal(2, first.GetProperty("total").GetInt32());
            Assert.Equal(locators.Select(l => l.GetProperty("digest").GetString()).Order(), new[] { first, second }.Select(p => p.GetProperty("items")[0].GetProperty("locator").GetProperty("digest").GetString()).Order());
            Assert.True((await host.CallAsync("eplan_selection_set", new() { ["mode"] = "clear" })).GetProperty("exact").GetBoolean());
            Assert.Equal(0, (await host.CallAsync("eplan_selection_get", new())).GetProperty("total").GetInt32());
        }
        Assert.Equal(before, LiveSnapshots.DesignFiles(session.Project));
        await session.UndoAsync();
        Assert.Equal(baseline, (await session.SnapshotAsync()).Hash);
        Assert.Contains((await session.SnapshotAsync()).Pages, p => p.Name == page);
    }

    private async Task InitializeEditorsAsync(PlanStepLiveSession session, string[] pages, Func<string, Task>? initialize = null)
    {
        // B11: first opening a newly drawn page changed Page.eod/eox. Initialize before the byte-preservation boundary;
        // still verify business content and every drawing locator across initialization, and every design file afterwards.
        string before = (await session.SnapshotAsync()).Hash;
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        foreach (string page in pages)
        {
            var args = new PageGetArgs { Project = session.Project, Page = page };
            PageDrawingGeometry drawing = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, args, TestContext.Current.CancellationToken);
            if (initialize is null)
            { await host.CallAsync("eplan_page_open", new() { ["project"] = session.Project, ["page"] = page }); }
            else
            { await initialize(page); }
            PageDrawingGeometry after = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, args, TestContext.Current.CancellationToken);
            Assert.Equal(drawing.Fingerprint(), after.Fingerprint());
        }
        Assert.Equal(before, (await session.SnapshotAsync()).Hash);
    }
}
