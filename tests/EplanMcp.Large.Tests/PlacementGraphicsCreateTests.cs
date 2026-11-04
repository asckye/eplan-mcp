using System.Globalization;
using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementGraphicsCreateTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task FourGraphicsKinds_ReadBackNativeGeometryAndOneUndoRestoresThePage()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        string page = Assert.Single(await selection.CreatePagesAsync(1));
        string file = "large/placement-graphics.bmp";
        string source = Path.Combine(host.Home, "imports", file);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, Convert.FromBase64String(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "placement-graphics.bmp.base64"))));
        string before = await Hash(bridge, page);
        LiveSnapshots.Snapshot projectBefore = await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable });
        double[][] points = [[20, 20], [30, 40], [40, 20]];
        double[] center = [80, 40];
        const double radius = 10, start = 30, end = 120;
        var request = new Dictionary<string, object?>
        {
            ["dry_run"] = false,
            ["project"] = host.Writable,
            ["page"] = page,
            ["snap_to_grid"] = false,
            ["items"] = new object[]
            {
                new { kind = "polyline", points },
                new { kind = "arc", center, radius_mm = radius, start_angle = start, end_angle = end },
                new { kind = "path_text", position = new[] { 40d, 80d }, height_mm = 3.5, contents = new Dictionary<string, string> { ["??_??"] = "Path function" } },
                new { kind = "image", file, corners = (double[][])[[100, 80], [120, 100]] },
            },
        };
        Evidence("request", request);
        var readArgs = new Dictionary<string, object?>
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["include_drawings"] = true,
            ["drawing_limit"] = 200,
            ["response_format"] = "detailed",
        };
        JsonElement result;
        try
        { result = await host.CallAsync("eplan_placement_create", request); }
        catch (Exception ex)
        {
            Evidence("failed result", ex.ToString());
            Evidence("readback after failed create", await host.CallAsync("eplan_page_get", readArgs));
            throw;
        }
        Evidence("result", result);
        JsonElement readback = await host.CallAsync("eplan_page_get", readArgs);
        Evidence("readback", readback);
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.False(result.GetProperty("recovered").GetBoolean());
        Assert.Equal(4, result.GetProperty("items").GetArrayLength());
        // Committing requires Verify to match native FileName (after PathMap resolution) with the full
        // imports source path. Only the relative input and basename cross the tool response boundary.
        Assert.Equal(file, result.GetProperty("items")[3].GetProperty("actual").GetProperty("file").GetString());
        JsonElement drawings = readback.GetProperty("drawings");
        Assert.False(drawings.GetProperty("truncated").GetBoolean());
        JsonElement Row(string kind) => Assert.Single(drawings.GetProperty("items").EnumerateArray(), row => row.GetProperty("kind").GetString() == kind);
        JsonElement polyline = Row("PolyLine");
        Assert.Equal("false", polyline.GetProperty("properties").GetProperty("closed").GetString());
        double[][] storedPoints = JsonSerializer.Deserialize<double[][]>(polyline.GetProperty("properties").GetProperty("points").GetString()!)!;
        Assert.Equal(points.Length, storedPoints.Length);
        for (int i = 0; i < points.Length; i++)
        { Assert.Equal(points[i], storedPoints[i]); }
        Outline([20, 20, 40, 40], polyline.GetProperty("bounds"));
        JsonElement arc = Row("Arc"), arcProperties = arc.GetProperty("properties");
        Assert.Equal(center, Coordinates(arc.GetProperty("location")));
        Assert.Equal(radius, Number(arcProperties, "radius_mm"), 8);
        Assert.Equal(start, Number(arcProperties, "start_angle"), 8);
        Assert.Equal(end, Number(arcProperties, "end_angle"), 8);
        Assert.Equal("false", arcProperties.GetProperty("sector").GetString());
        // XML has no Arc endpoint members. This asymmetric arc distinguishes both angular sign and sweep.
        double[] first = Endpoint(center, radius, Number(arcProperties, "start_angle"));
        double[] last = Endpoint(center, radius, Number(arcProperties, "end_angle"));
        Evidence("arc computed endpoints", new { first, last });
        Outline([last[0], first[1], first[0], center[1] + radius], arc.GetProperty("bounds"));
        JsonElement pathText = Row("PathText");
        Assert.Equal([40d, 80d], Coordinates(pathText.GetProperty("location")));
        Assert.Equal(3.5, Number(pathText.GetProperty("properties"), "height_mm"), 8);
        Assert.Equal("??_??@Path function;", pathText.GetProperty("properties").GetProperty("text").GetString());
        JsonElement image = Row("Image");
        Outline([100, 80, 120, 100], image.GetProperty("bounds"));
        Assert.Equal(Path.GetFileName(source), image.GetProperty("properties").GetProperty("file").GetString(), ignoreCase: true);
        Assert.NotEqual(before, await Hash(bridge, page));
        await selection.UndoAsync();
        Assert.Equal(before, await Hash(bridge, page));
        Assert.Equal(projectBefore.Hash, (await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable })).Hash);
    }

    [Fact]
    public async Task PathText_OmittedHeightReadsDefaultSentinel()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        string page = Assert.Single(await selection.CreatePagesAsync(1));
        var request = new Dictionary<string, object?>
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["dry_run"] = false,
            ["snap_to_grid"] = false,
            ["items"] = new[] { new { kind = "path_text", position = new[] { 40d, 80d }, contents = new Dictionary<string, string> { ["??_??"] = "Default path function" } } },
        };
        JsonElement readback = await CreateAndReadback(request);
        JsonElement row = Assert.Single(readback.GetProperty("drawings").GetProperty("items").EnumerateArray(), r => r.GetProperty("kind").GetString() == "PathText");
        Assert.Equal(-16002, Number(row.GetProperty("properties"), "height_mm"));
        Assert.Equal("??_??@Default path function;", row.GetProperty("properties").GetProperty("text").GetString());
    }

    [Fact]
    public async Task EllipseAndFullCircle_ReadBackNativeKindsAndGeometry()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        string page = Assert.Single(await selection.CreatePagesAsync(1));
        var request = new Dictionary<string, object?>
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["dry_run"] = false,
            ["snap_to_grid"] = false,
            ["items"] = new object[]
            {
                new { kind = "ellipse", center = new[] { 40d, 40d }, radius_x_mm = 1.43748, radius_y_mm = 1.43748, angle = 90 },
                new { kind = "arc", center = new[] { 80d, 40d }, radius_mm = 8, start_angle = 0, end_angle = 360 },
            },
        };
        JsonElement readback = await CreateAndReadback(request);
        JsonElement rows = readback.GetProperty("drawings").GetProperty("items");
        JsonElement ellipse = Assert.Single(rows.EnumerateArray(), r => r.GetProperty("properties").TryGetProperty("arc_kind", out JsonElement kind) && kind.GetString() == "Ellipse").GetProperty("properties");
        Assert.Equal(1.43748, Number(ellipse, "radius_x_mm"), 7);
        Assert.Equal(1.43748, Number(ellipse, "radius_y_mm"), 7);
        Assert.Equal(Math.PI / 2, Number(ellipse, "ellipse_angle"), 6);
        Assert.Equal(0, Number(ellipse, "radius_mm"));
        JsonElement circle = Assert.Single(rows.EnumerateArray(), r => r.GetProperty("properties").TryGetProperty("arc_kind", out JsonElement kind) && kind.GetString() == "Circle").GetProperty("properties");
        Assert.Equal(8, Number(circle, "radius_mm"), 8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllPrimitiveStyles_ReadBackIncludingDefaultSentinels(bool defaults)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        var pen = new { color = defaults ? -16002 : 1, width_mm = defaults ? -16002 : 0.25, style = defaults ? -16002 : 1, pattern_length = defaults ? -16002 : 8, line_end = 2 };
        var request = new Dictionary<string, object?>
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["dry_run"] = false,
            ["snap_to_grid"] = false,
            ["response_format"] = "detailed",
            ["items"] = new object[] {
                new { kind = "line", points = (int[][])[[20, 40], [40, 40]], layer = 108, pen },
                new { kind = "rectangle", corners = (int[][])[[60, 40], [80, 60]], angle = 90, layer = 108, pen },
                new { kind = "text", position = (int[])[100, 80], height_mm = 3.5, contents = new Dictionary<string,string> { ["??_??"] = "Style" }, angle = 90, alignment = "MiddleLeft", layer = 108, pen },
                new { kind = "path_text", position = (int[])[140, 80], height_mm = 3.5, contents = new Dictionary<string,string> { ["??_??"] = "Path style" }, angle = 90, alignment = "BaseLineCenter", layer = 108, pen },
                new { kind = "polyline", points = (int[][])[[180, 40], [200, 60], [220, 40]], closed = true, layer = 108, pen } }
        };
        JsonElement rows = (await CreateAndReadback(request)).GetProperty("drawings").GetProperty("items");
        foreach (string kind in new[] { "Line", "Rectangle", "Text", "PathText", "PolyLine" })
        {
            JsonElement properties = Assert.Single(rows.EnumerateArray(), r => r.GetProperty("kind").GetString() == kind).GetProperty("properties");
            Assert.Equal(108, Number(properties, "layer"));
            foreach (JsonProperty field in JsonSerializer.SerializeToElement(pen).EnumerateObject())
            { Assert.Equal(field.Value.GetDouble(), Number(properties, "pen_" + field.Name), 7); }
            if (kind is "Text" or "PathText" or "Rectangle")
            { Assert.Equal(90, Number(properties, "angle"), 7); }
            if (kind == "PolyLine")
            { Assert.Equal("true", properties.GetProperty("closed").GetString()); }
            if (kind == "Text")
            { Assert.Equal("MiddleLeft", properties.GetProperty("alignment").GetString()); }
            if (kind == "PathText")
            { Assert.Equal("BaseLineCenter", properties.GetProperty("alignment").GetString()); }
        }
    }

    private async Task<JsonElement> CreateAndReadback(Dictionary<string, object?> request)
    {
        var read = new Dictionary<string, object?> { ["project"] = host.Writable, ["page"] = request["page"], ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" };
        Evidence("request", request);
        JsonElement result;
        try
        { result = await host.CallAsync("eplan_placement_create", request); }
        catch (Exception ex)
        {
            Evidence("failed result", ex.ToString());
            Evidence("readback after failed create", await host.CallAsync("eplan_page_get", read));
            throw;
        }
        Evidence("result", result);
        JsonElement readback = await host.CallAsync("eplan_page_get", read);
        Evidence("readback", readback);
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        return readback;
    }

    private async Task<string> Hash(BridgeClient bridge, string page) => SnapshotCanonical.Sha256(JsonSerializer.Serialize(
        await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = host.Writable, Page = page }, TestContext.Current.CancellationToken)));
    private static void Evidence(string label, object value) => TestContext.Current.TestOutputHelper?.WriteLine(label + ": " + JsonSerializer.Serialize(value));
    private static double Number(JsonElement properties, string name) => double.Parse(properties.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
    private static double[] Coordinates(JsonElement point) => [.. point.EnumerateArray().Select(n => n.GetDouble())];
    private static double[] Endpoint(double[] center, double radius, double angle) => [center[0] + radius * Math.Cos(angle * Math.PI / 180), center[1] + radius * Math.Sin(angle * Math.PI / 180)];
    private static void Outline(double[] expected, JsonElement bounds)
    {
        double[] actual = Coordinates(bounds);
        for (int i = 0; i < 4; i++)
        { Assert.InRange(i < 2 ? expected[i] - actual[i] : actual[i] - expected[i], -1e-8, 1); }
    }
}
