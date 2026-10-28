using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_device_place on the write copy (README.md), never on the read tests' fixture (docs/tools/eplan_device_place.md,
/// section 7). The part is the contactor S20 placed, SIE.3RH2122-1HB40 from the development machine's parts database
/// (A26, A27). Each run places on a new page after the last one of =GAA+A1&amp;EFS1, with a tag made from the page's
/// number, so runs do not collide; the copy keeps the devices. What S20 did not try goes to the test output: where
/// the point puts the device, and an image of the page to look at.
/// </summary>
[Trait("Size", "Large")]
public sealed class DevicePlaceTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private const string Part = "SIE.3RH2122-1HB40";
    private const string Prefix = "=GAA+A1&EFS1/";

    [Fact]
    public async Task ALocationBoxAroundAnExistingCoil_ReadsBackDashedWithoutRenaming_OneUndoRestores()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string project = await eplan.WriteProjectAsync();
        string page = await NewPageAsync(project);
        string tag = "=GAA+A1-KB" + page[Prefix.Length..];
        await eplan.CallAsync("eplan_device_place", new()
        {
            ["project"] = project,
            ["page"] = page,
            ["library"] = "IEC_symbol",
            ["symbol"] = "K",
            ["symbol_variant"] = 0,
            ["x"] = 80,
            ["y"] = 160,
            ["tag"] = tag
        });
        JsonElement coilBefore = (await eplan.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag })).GetProperty("device");
        LiveSnapshots.Snapshot before = await selection.SnapshotAsync();
        Dictionary<string, object?> args = new()
        {
            ["project"] = project,
            ["page"] = page,
            ["location_box"] = "+P" + page[Prefix.Length..],
            ["x"] = 60,
            ["y"] = 140,
            ["corner_x"] = 100,
            ["corner_y"] = 180
        };
        async Task<JsonElement> Drawings() => (await eplan.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true })).GetProperty("drawings").GetProperty("items");
        JsonElement drawingsBefore = await Drawings();
        JsonElement preview = await eplan.CallAsync("eplan_device_place", new(args) { ["dry_run"] = true });
        Assert.Equal(drawingsBefore.GetRawText(), (await Drawings()).GetRawText());
        JsonElement placed = await eplan.CallAsync("eplan_device_place", args);
        Assert.Equal(("committed", "AI: eplan_device_place"), (placed.GetProperty("execution").GetString(), placed.GetProperty("undo").GetString()));
        JsonElement box = placed.GetProperty("locationBox");
        Assert.Equal(args["location_box"], box.GetProperty("structure").GetString());
        Assert.Equal("dashed", box.GetProperty("outline").GetString());
        Assert.Equal([60d, 140d, 100d, 180d], box.GetProperty("bounds").EnumerateArray().Select(n => n.GetDouble()));
        Assert.Empty(box.GetProperty("affectedDevices").EnumerateArray());
        Assert.Equal(preview.GetProperty("locationBox").GetRawText(), box.GetRawText());
        Assert.Single((await Drawings()).EnumerateArray(), p => p.GetProperty("kind").GetString() == "LocationBox");
        JsonElement coilAfter = (await eplan.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag })).GetProperty("device");
        Assert.Equal(coilBefore.GetRawText(), coilAfter.GetRawText());
        await selection.UndoAsync();
        Assert.Equal(drawingsBefore.GetRawText(), (await Drawings()).GetRawText());
        Assert.Equal(before.Hash, (await selection.SnapshotAsync()).Hash);
        Assert.Equal(1, await DevicesOnAsync(project, page));
    }

    [Fact]
    public async Task ASymbolCoilWithoutPart_DisplaysPrefixedTagAndDesignations_AndRejectsRepeatAndConflict()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string project = await eplan.WriteProjectAsync();
        string page = await NewPageAsync(project);
        LiveSnapshots.Snapshot before = await selection.SnapshotAsync();
        string tag = "-KS" + page[Prefix.Length..];
        Dictionary<string, object?> args = new()
        {
            ["project"] = project,
            ["page"] = page,
            ["library"] = "IEC_symbol",
            ["symbol"] = "K",
            ["symbol_variant"] = 0,
            ["x"] = 80,
            ["y"] = 160,
            ["tag"] = tag,
            ["display_properties"] = JsonSerializer.SerializeToElement(new[] { new { property_id = 20214, offset_x_mm = 4, offset_y_mm = -4 } }),
            ["connection_designations"] = new[] { "A1", "A2" },
        };
        JsonElement preview = await eplan.CallAsync("eplan_device_place", new(args) { ["dry_run"] = true });
        Assert.Equal(0, await DevicesOnAsync(project, page));
        JsonElement placed = await eplan.CallAsync("eplan_device_place", args);
        Assert.Equal(("committed", 1, 0, ""), (placed.GetProperty("execution").GetString(), placed.GetProperty("functions").GetInt32(), placed.GetProperty("unplaced").GetInt32(), placed.GetProperty("part").GetString()));
        Assert.Equal(("=GAA+A1" + tag, tag, "Coil"), (placed.GetProperty("device").GetProperty("tag").GetString(), placed.GetProperty("device").GetProperty("visibleTag").GetString(), placed.GetProperty("device").GetProperty("category").GetString()));
        Assert.Equal(preview.GetProperty("symbol").GetRawText(), placed.GetProperty("symbol").GetRawText());
        JsonElement read = await eplan.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = "=GAA+A1" + tag });
        Assert.Empty(read.GetProperty("parts").EnumerateArray());
        Assert.Equal(["A1", "A2"], read.GetProperty("device").GetProperty("pins").EnumerateArray().Select(p => p.GetString()));
        JsonElement drawing = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true })).GetProperty("drawings");
        JsonElement label = Assert.Single(drawing.GetProperty("items").EnumerateArray(), p => p.GetProperty("kind").GetString() == "PropertyPlacement" && p.GetProperty("properties").TryGetProperty("display", out JsonElement text) && text.GetString() == tag);
        Assert.Equal([84.0, 156.0], label.GetProperty("location").EnumerateArray().Select(v => v.GetDouble()));
        foreach (string duplicate in new[] { tag, ("=GAA+A1" + tag).ToLowerInvariant() })
        {
            JsonElement error = await eplan.ErrorAsync("eplan_device_place", new(args) { ["tag"] = duplicate });
            Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        }
        Assert.Equal(1, await DevicesOnAsync(project, page));
        JsonElement image = await eplan.CallAsync("eplan_export_image", new() { ["project"] = project, ["pages"] = JsonSerializer.SerializeToElement(new[] { page }) });
        TestContext.Current.TestOutputHelper?.WriteLine($"Check {image.GetProperty("directory").GetString()}: {tag} at (84, 156), coil at (80, 160).");
        await selection.UndoAsync();
        Assert.Equal(0, await DevicesOnAsync(project, page));
        Assert.Equal(before.Hash, (await selection.SnapshotAsync()).Hash);
    }

    [Fact]
    public async Task AContactor_OnANewPage_IsPlacedWithItsTagInThePagesStructure_AndTheTagOnlyOnce()
    {
        string project = await eplan.WriteProjectAsync();
        string page = await NewPageAsync(project);
        string tag = "-KF9" + page[Prefix.Length..];
        Dictionary<string, object?> args = new() { ["project"] = project, ["page"] = page, ["part"] = Part, ["x"] = 80, ["y"] = 160, ["tag"] = tag };

        JsonElement dryRun = await eplan.CallAsync("eplan_device_place", new(args) { ["dry_run"] = true });
        JsonElement placed = await eplan.CallAsync("eplan_device_place", args);
        JsonElement again = await eplan.ErrorAsync("eplan_device_place", args);
        JsonElement device = (await eplan.CallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = "=GAA+A1" + tag })).GetProperty("device");

        // The plan reads the parts database without placing anything; the tag takes the page's structure.
        Assert.Equal(("not_started", "=GAA+A1" + tag, "1"), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("device").GetProperty("tag").GetString(), dryRun.GetProperty("variant").GetString()));
        Assert.Equal(JsonValueKind.Null, dryRun.GetProperty("functions").ValueKind);

        // S20 (A26): the coil placed, its 4 contacts unplaced; renamed, the device shows the tag.
        Assert.Equal(("committed", "AI: eplan_device_place"), (placed.GetProperty("execution").GetString(), placed.GetProperty("undo").GetString()));
        JsonElement read = placed.GetProperty("device");
        Assert.Equal(("=GAA+A1" + tag, tag, "Coil"), (read.GetProperty("tag").GetString(), read.GetProperty("visibleTag").GetString(), read.GetProperty("category").GetString()));
        Assert.Equal((5, 4), (placed.GetProperty("functions").GetInt32(), placed.GetProperty("unplaced").GetInt32()));
        Assert.Equal(("=GAA+A1" + tag, page), (device.GetProperty("tag").GetString(), device.GetProperty("page").GetString()));

        // Not idempotent: the tag is taken now (tool spec 6), and nothing more is placed.
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (again.GetProperty("reason").GetString(), again.GetProperty("execution").GetString()));
        Assert.Equal(1, await DevicesOnAsync(project, page));

        JsonElement image = await eplan.CallAsync("eplan_export_image", new() { ["project"] = project, ["pages"] = JsonSerializer.SerializeToElement(new[] { page }) });
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{page}: {placed.GetProperty("device")}; hint: {placed.GetProperty("hint")}. Check in {image.GetProperty("directory").GetString()} that the coil shows {tag} near (80, 160) mm with its contact mirror.");
    }

    [Fact]
    public async Task APartNotInTheDatabase_AndAVariantItLacks_ChangeNothing()
    {
        string project = await eplan.WriteProjectAsync();
        string page = await NewPageAsync(project);
        Dictionary<string, object?> args = new() { ["project"] = project, ["page"] = page, ["part"] = Part, ["x"] = 80, ["y"] = 160, ["tag"] = "-KF9" + page[Prefix.Length..] };

        JsonElement missing = await eplan.ErrorAsync("eplan_device_place", new(args) { ["part"] = "SIE.NO-SUCH-PART-0000" });
        JsonElement variant = await eplan.ErrorAsync("eplan_device_place", new(args) { ["variant"] = "99" });

        Assert.Equal((ErrorReason.PartNotFound, ExecutionState.NotStarted), (missing.GetProperty("reason").GetString(), missing.GetProperty("execution").GetString()));
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (variant.GetProperty("reason").GetString(), variant.GetProperty("execution").GetString()));
        Assert.Contains("has the variants 1", variant.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, await DevicesOnAsync(project, page));
    }

    [Fact]
    public async Task TheCopyOutsideScratch_IsPolicyDenied()
    {
        JsonElement error = await host.ErrorAsync(
            "eplan_device_place",
            new() { ["project"] = host.Outside, ["page"] = Prefix + "1", ["part"] = Part, ["x"] = 80, ["y"] = 160, ["tag"] = "-KF9999" });

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    /// <summary>A new circuit page after the last numbered page of =GAA+A1&amp;EFS1 in the copy.</summary>
    private async Task<string> NewPageAsync(string project)
    {
        int last = 0;
        string? cursor = null;
        do
        {
            Dictionary<string, object?> args = new() { ["project"] = project, ["name_prefix"] = Prefix, ["limit"] = 200 };
            if (cursor is not null)
            {
                args["cursor"] = cursor;
            }

            JsonElement list = await eplan.CallAsync("eplan_page_list", args);
            foreach (JsonElement page in list.GetProperty("pages").EnumerateArray())
            {
                if (int.TryParse(page.GetProperty("name").GetString()![Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                {
                    last = Math.Max(last, number);
                }
            }

            cursor = list.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        string name = Prefix + (last + 1).ToString(CultureInfo.InvariantCulture);
        await eplan.CallAsync("eplan_page_create", new() { ["project"] = project, ["pages"] = JsonSerializer.SerializeToElement(new[] { new { name } }) });
        return name;
    }

    private async Task<int> DevicesOnAsync(string project, string page) =>
        (await eplan.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page })).GetProperty("deviceCount").GetInt32();
}
