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
