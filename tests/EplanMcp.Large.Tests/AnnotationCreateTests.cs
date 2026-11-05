using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class AnnotationCreateTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    private static readonly double[] _circleCenter = [300, 50];

    [Fact]
    public async Task AllAnnotationKinds_PreviewReadbackExportDeleteAndSingleUndo()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(host.Writable));
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        var read = new Dictionary<string, object?> { ["project"] = host.Writable, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" };
        await Call("eplan_placement_create", new()
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["dry_run"] = false,
            ["snap_to_grid"] = false,
            ["items"] = new[] { new { kind = "arc", center = _circleCenter, radius_mm = 6, start_angle = 0, end_angle = 360 } }
        });
        JsonElement circle = Assert.Single((await Find("Arc")).GetProperty("items").EnumerateArray()).GetProperty("locator");
        var request = new Dictionary<string, object?>
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["snap_to_grid"] = false,
            ["layout_guard"] = "warn",
            ["response_format"] = "detailed",
            ["items"] = new object[]
            {
                new { kind = "hyperlink", position = new[] { 20d, 50d }, link = "https://example.com/annotations", contents = new Dictionary<string, string> { ["??_??"] = "Annotation link" } },
                new { kind = "qr_code", position = new[] { 65d, 50d }, size_mm = 10, text = "annotation-qr" },
                new { kind = "spline", points = (double[][])[[100, 50], [115, 70], [130, 50]] },
                new { kind = "sector", center = new[] { 240d, 50d }, radius_mm = 8, start_angle = 30, end_angle = 120 },
                new { kind = "dimension", dimension_type = "linear", first = new[] { 20d, 120d }, second = new[] { 50d, 120d }, end = new[] { 20d, 105d } },
                new { kind = "dimension", dimension_type = "angular", first = new[] { 90d, 120d }, second = new[] { 110d, 120d }, end = new[] { 110d, 140d } },
                new { kind = "dimension", dimension_type = "baseline", points = (double[][])[[150, 120], [170, 120], [190, 120]], end = new[] { 150d, 105d } },
                new { kind = "dimension", dimension_type = "incremental", points = (double[][])[[220, 120], [240, 120], [260, 120]], end = new[] { 220d, 105d } },
                new { kind = "dimension", dimension_type = "continued", points = (double[][])[[20, 180], [40, 180], [60, 180]], end = new[] { 20d, 165d } },
                new { kind = "dimension", dimension_type = "circle", arc = circle, end = new[] { 315d, 65d } },
                new { kind = "placeholder", position = new[] { 175d, 50d }, name = "Annotation placeholder", records = new[] { "variant-a", "variant-b" }, variables = new[] { "Voltage" } },
                new { kind = "macro_box", position = new[] { 205d, 50d }, macro_name = "annotation-test.ema" }
            }
        };
        string before = await Hash();
        string projectBefore = (await session.SnapshotAsync()).Hash;
        JsonElement preview = await Call("eplan_annotation_create", request);
        Assert.Equal(ExecutionState.NotStarted, preview.GetProperty("execution").GetString());
        Assert.True(preview.GetProperty("dryRun").GetBoolean());
        Assert.Equal(12, preview.GetProperty("items").GetArrayLength());
        Assert.Equal(before, await Hash());
        Assert.Equal(projectBefore, (await session.SnapshotAsync()).Hash);
        request["dry_run"] = true;
        Assert.Equal(ExecutionState.NotStarted, (await Call("eplan_annotation_create", request)).GetProperty("execution").GetString());
        Assert.Equal(before, await Hash());
        request["dry_run"] = false;
        JsonElement result = await Call("eplan_annotation_create", request);
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Equal(12, result.GetProperty("items").GetArrayLength());
        JsonElement[] rows = (await Read()).GetProperty("drawings").GetProperty("items").EnumerateArray().ToArray();
        JsonElement Row(string kind) => Assert.Single(rows, r => r.GetProperty("kind").GetString() == kind);
        Assert.Equal("https://example.com/annotations", Row("HyperLink").GetProperty("properties").GetProperty("link").GetString());
        Assert.Equal("annotation-qr", Row("QRCode").GetProperty("properties").GetProperty("text").GetString());
        Assert.Equal("Annotation placeholder", Row("PlaceHolder").GetProperty("properties").GetProperty("name").GetString());
        Assert.Equal("annotation-test.ema", Row("MacroBox").GetProperty("properties").GetProperty("macro_name").GetString());
        Assert.Single(rows, r => r.GetProperty("kind").GetString() == "Arc" && r.GetProperty("properties").GetProperty("sector").GetString() == "true");
        Assert.Equal(8, rows.Count(r => r.GetProperty("kind").GetString() == "DimensionItem"));
        Assert.Single(rows, r => r.GetProperty("kind").GetString() == "DimensionCircle");
        Assert.All(rows.Where(r => r.GetProperty("kind").GetString() is "DimensionItem" or "DimensionCircle"), r =>
            Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("properties").GetProperty("dimension_text").GetString())));
        foreach (string kind in new[] { "HyperLink", "QRCode", "Spline", "Arc", "DimensionItem", "DimensionCircle", "PlaceHolder", "MacroBox" })
        {
            JsonElement found = await Find(kind);
            Assert.Equal("supported", found.GetProperty("status").GetString());
            Assert.False(found.GetProperty("truncated").GetBoolean());
            JsonElement[] matching = rows.Where(r => r.GetProperty("kind").GetString() == kind).ToArray();
            Assert.Equal(matching.Length, found.GetProperty("items").GetArrayLength());
            foreach (JsonElement item in found.GetProperty("items").EnumerateArray())
            { Assert.Contains(matching, r => SameId(r.GetProperty("locator"), item.GetProperty("locator"))); }
        }
        JsonElement image = await Call("eplan_export_image", new() { ["project"] = host.Writable, ["pages"] = new[] { page }, ["format"] = "png", ["width_px"] = 2400 });
        Assert.False(image.GetProperty("truncated").GetBoolean());
        Assert.Equal(1, image.GetProperty("fileCount").GetInt32());
        JsonElement exported = Assert.Single(image.GetProperty("files").EnumerateArray());
        string imagePath = Path.Combine(image.GetProperty("directory").GetString()!, exported.GetProperty("path").GetString()!);
        byte[] png = await File.ReadAllBytesAsync(imagePath, TestContext.Current.CancellationToken);
        Assert.True(png.Length > 8);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Evidence("annotation page image", imagePath);
        string created = await Hash();
        JsonElement target = Row("QRCode").GetProperty("locator");
        var deletion = new Dictionary<string, object?> { ["project"] = host.Writable, ["page"] = page, ["items"] = new[] { target } };
        Assert.Equal(ExecutionState.NotStarted, (await Call("eplan_placement_delete", deletion)).GetProperty("execution").GetString());
        Assert.Equal(created, await Hash());
        host.Approvals.Approve("eplan_placement_delete", host.Writable, new[] { $"delete drawing {page}:{target.GetProperty("type")}/{target.GetProperty("oid")}" });
        deletion["dry_run"] = false;
        Assert.Equal(ExecutionState.Committed, (await Call("eplan_placement_delete", deletion)).GetProperty("execution").GetString());
        Assert.DoesNotContain((await Read()).GetProperty("drawings").GetProperty("items").EnumerateArray(), r => SameId(r.GetProperty("locator"), target));
        Assert.Empty((await Find("QRCode")).GetProperty("items").EnumerateArray());
        await session.UndoAsync();
        Assert.Equal(created, await Hash());
        await session.UndoAsync();
        Assert.Equal(before, await Hash());
        Assert.Equal(projectBefore, (await session.SnapshotAsync()).Hash);

        async Task<string> Hash() => SnapshotCanonical.Sha256(JsonSerializer.Serialize(await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = host.Writable, Page = page }, TestContext.Current.CancellationToken)));
        Task<JsonElement> Read() => Call("eplan_page_get", read);
        Task<JsonElement> Find(string kind) => Call("eplan_object_find", new() { ["project"] = host.Writable, ["page"] = page, ["object_type"] = kind, ["limit"] = 200 });
        async Task<JsonElement> Call(string tool, Dictionary<string, object?> args)
        {
            Evidence("request", new { tool, args });
            try
            { JsonElement value = await host.CallAsync(tool, args); Evidence("result", value); return value; }
            catch (Exception ex)
            {
                Evidence("failed result", ex.ToString());
                if (tool != "eplan_page_get")
                { Evidence("readback after failure", await host.CallAsync("eplan_page_get", read)); }
                throw;
            }
        }
    }

    private static bool SameId(JsonElement left, JsonElement right) => left.GetProperty("type").GetInt32() == right.GetProperty("type").GetInt32()
        && left.GetProperty("oid").GetInt64() == right.GetProperty("oid").GetInt64();
    private static void Evidence(string label, object value) => TestContext.Current.TestOutputHelper?.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}
