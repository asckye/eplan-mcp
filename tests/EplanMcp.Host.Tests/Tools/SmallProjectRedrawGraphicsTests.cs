using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawGraphicsTests
{
    [Theory]
    [InlineData(1.43748000000004, 1.43748000000004)]
    [InlineData(4, 2)]
    public async Task Ellipse_WithZeroCircularRadius_UsesNativeRadiiAndRotation(double firstRadius, double secondRadius)
    {
        JObject source = Arc("Ellipse");
        source["radius"] = 0.0;
        source["firstRadius"] = firstRadius;
        source["secondRadius"] = secondRadius;
        source["ellipseAngle"] = 1.5707963267948966;

        (JObject[] items, JObject report) = await DrawAsync(source);

        JObject item = Assert.Single(items);
        Assert.Equal("ellipse", (string?)item["kind"]);
        Assert.True(JToken.DeepEquals(new JArray(368.19053251362, 90.45769752315101), item["center"]));
        Assert.Equal(firstRadius, (double)item["radius_x_mm"]!);
        Assert.Equal(secondRadius, (double)item["radius_y_mm"]!);
        Assert.Equal(90, (double)item["angle"]!, 10);
        Assert.Null(item["radius_mm"]);
        Assert.Null(item["start_angle"]);
        Assert.Null(item["end_angle"]);
        Assert.Empty((JArray)report["warnings"]!);
        Assert.Empty((JArray)report["noToolTypes"]!);
    }

    [Theory]
    [InlineData("Circle", 0, 360)]
    [InlineData("Arc", 90, 180)]
    public async Task CircularGeometry_PreservesCircleAndArcAngleMappings(string kind, double start, double end)
    {
        JObject source = Arc(kind);
        source["radius"] = 3.5;
        source["startAngle"] = Math.PI / 2;
        source["endAngle"] = Math.PI;

        (JObject[] items, _) = await DrawAsync(source);

        JObject item = Assert.Single(items);
        Assert.Equal("arc", (string?)item["kind"]);
        Assert.Equal(3.5, (double)item["radius_mm"]!);
        Assert.Equal(start, (double)item["start_angle"]!, 10);
        Assert.Equal(end, (double)item["end_angle"]!, 10);
        Assert.Null(item["radius_x_mm"]);
        Assert.Null(item["radius_y_mm"]);
    }

    [Theory]
    [InlineData(-16002, false)]
    [InlineData(2.5, true)]
    public async Task PathText_InheritedHeightIsOmittedAndExplicitHeightIsPreserved(double height, bool expectedHeight)
    {
        JObject source = Text("PathText", new JObject { ["en_US"] = "Example" });
        source["location"] = Point(96, 48);
        source["height"] = height;

        (JObject[] items, _) = await DrawAsync(source);

        JObject item = Assert.Single(items);
        Assert.Equal("path_text", (string?)item["kind"]);
        Assert.Equal(expectedHeight, item.ContainsKey("height_mm"));
        if (expectedHeight)
        { Assert.Equal(height, (double)item["height_mm"]!); }
        Assert.True(JToken.DeepEquals(source["content"], item["contents"]));
        Assert.True(JToken.DeepEquals(new JArray(96d, 48d), item["position"]));
    }

    [Theory]
    [InlineData("Text", "\"\"")]
    [InlineData("PathText", "\"\"")]
    [InlineData("Text", "{}")]
    [InlineData("PathText", "{\"en_US\":\"\",\"de_DE\":\"\"}")]
    public async Task EmptyText_IsReportedWithoutCallingPlacementCreate(string type, string content)
    {
        JObject source = Text(type, JToken.Parse(content));

        (JObject[] items, JObject report) = await DrawAsync(source);

        Assert.Empty(items);
        JObject note = Assert.IsType<JObject>(Assert.Single((JArray)report["notes"]!));
        Assert.Equal(type, (string?)note["objectType"]);
        Assert.Equal("1", (string?)note["oid"]);
        Assert.Contains("Empty text", (string?)note["reason"], StringComparison.Ordinal);
        Assert.Empty((JArray)report["warnings"]!);
        Assert.Equal(0, (int)report["failedToolCallCount"]!);
    }

    [Fact]
    public async Task Text_WithAnEmptyLanguageAndAValueInAnotherLanguage_IsPreserved()
    {
        JObject source = Text("Text", new JObject { ["en_US"] = "", ["de_DE"] = "Example" });

        (JObject[] items, _) = await DrawAsync(source);

        JObject item = Assert.Single(items);
        Assert.Equal("text", (string?)item["kind"]);
        Assert.True(JToken.DeepEquals(source["content"], item["contents"]));
        Assert.Equal(2.5, (double)item["height_mm"]!);
    }

    private static async Task<(JObject[] Items, JObject Report)> DrawAsync(params JObject[] placements)
    {
        var source = new JObject
        {
            ["name"] = "/1",
            ["pageType"] = "Circuit",
            ["placements"] = new JArray(placements),
            ["connections"] = new JArray(),
        };
        var items = new List<JObject>();
        Task<JObject> CallAsync(string tool, JObject arguments)
        {
            if (tool == "eplan_placement_create")
            { items.AddRange(((JArray)arguments["items"]!).OfType<JObject>().Select(item => (JObject)item.DeepClone())); }
            return Task.FromResult(new JObject
            {
                ["execution"] = "committed",
                ["drawings"] = new JObject { ["items"] = new JArray(), ["truncated"] = false },
            });
        }
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        return (items.ToArray(), report.ToJson());
    }

    private static JObject Arc(string kind) => new()
    {
        ["type"] = "Arc",
        ["oid"] = 1,
        ["kind"] = kind,
        ["center"] = Point(368.19053251362, 90.45769752315101),
        ["radius"] = 0.0,
        ["startAngle"] = 0.0,
        ["endAngle"] = 0.0,
        ["sector"] = false,
    };

    private static JObject Text(string type, JToken content) => new()
    {
        ["type"] = type,
        ["oid"] = 1,
        ["location"] = Point(10, 20),
        ["content"] = content,
        ["height"] = 2.5,
        ["angle"] = 0,
    };

    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
}
