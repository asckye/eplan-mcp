using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawRoundFourOverviewTests
{
    [Fact]
    public async Task LiveGroupedOverviewRectangle_IsRetainedWithoutDuplicateOrDeletion()
    {
        JObject rectangle = new()
        {
            ["type"] = "Rectangle",
            ["oid"] = 1,
            ["location"] = Point(246, 74),
            ["lowerLeft"] = Point(246, 74),
            ["upperRight"] = Point(412, 78),
            ["boundingBox"] = new JArray(Point(246, 74), Point(412, 78)),
            ["angle"] = 0,
            ["layer"] = 100,
            ["pen"] = new JObject { ["color"] = -16002, ["width"] = 0.13000000000000012, ["style"] = -16002, ["patternLength"] = 16002, ["lineEndType"] = 0 },
        };
        JObject actual = Drawing(rectangle, 2);
        actual["properties"] = new JObject
        {
            ["grouped"] = "true",
            ["angle"] = "0",
            ["layer"] = "100",
            ["pen_color"] = "-16002",
            ["pen_width_mm"] = "0.13000000000000012",
            ["pen_style"] = "-16002",
            ["pen_pattern_length"] = "16002",
            ["pen_line_end"] = "0",
        };
        var calls = new List<(string Tool, JObject Args)>();
        JObject page = Page(rectangle);
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (_, _) => Success([actual]));

        await driver.DrawPageAsync(page, [page]);

        Assert.DoesNotContain(calls, call => call.Tool is "eplan_placement_create" or "eplan_placement_delete");
    }

    [Fact]
    public async Task GroupedExtraGraphic_IsDeletedWhileParentedMemberIsExcluded()
    {
        JObject extra = Drawing(Line(1, 10, 20), 101);
        JObject member = Drawing(Line(2, 30, 20), 102);
        member["parent"] = new JObject { ["type"] = 144, ["oid"] = 103 };
        var calls = new List<(string Tool, JObject Args)>();
        JObject page = Page();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (_, _) => Success([extra, member]));

        await driver.DrawPageAsync(page, [page]);

        Assert.Equal(101, (int)Assert.Single(calls, call => call.Tool == "eplan_placement_delete").Args["items"]![0]!["oid"]!);
    }

    [Fact]
    public async Task MissingBlock_RebuildsTextAndMissingGraphicsBeforeGroupingAbsoluteMembers()
    {
        JObject line = Line(1, 0, 0);
        JObject text = new()
        {
            ["type"] = "Text",
            ["oid"] = 2,
            ["location"] = Point(10, 0),
            ["height"] = 2.5,
            ["angle"] = 0,
            ["content"] = new JObject { ["de_DE"] = "Beispiel", ["en_US"] = "Example" },
        };
        JObject rectangle = new()
        {
            ["type"] = "Rectangle",
            ["oid"] = 3,
            ["location"] = Point(20, 0),
            ["lowerLeft"] = Point(20, 0),
            ["upperRight"] = Point(22, 2),
            ["boundingBox"] = new JArray(Point(20, 0), Point(22, 2)),
            ["angle"] = 0,
        };
        JObject block = new()
        {
            ["type"] = "Block",
            ["oid"] = 4,
            ["name"] = "FixtureBlock",
            ["location"] = Point(100, 200),
            ["members"] = new JArray(line, text, rectangle),
        };
        JObject absoluteText = (JObject)text.DeepClone();
        absoluteText["location"] = Point(110, 200);
        var actual = new List<JObject> { Drawing(Line(1, 100, 200), 101), Drawing(absoluteText, 102) };
        actual[1]["properties"]!["text"] = "de_DE@Beispiel;";
        var calls = new List<(string Tool, JObject Args)>();
        JObject page = Page(block);
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (tool, args) =>
        {
            if (tool == "eplan_placement_delete")
            {
                var ids = ((JArray)args["items"]!).Select(item => (int)item["oid"]!).ToHashSet();
                actual.RemoveAll(item => ids.Contains((int)item["locator"]!["oid"]!));
            }
            if (tool == "eplan_placement_create")
            {
                foreach (JObject item in ((JArray)args["items"]!).OfType<JObject>())
                {
                    if ((string?)item["kind"] == "text")
                    { actual.Add(Drawing(absoluteText, 202)); }
                    if ((string?)item["kind"] == "rectangle")
                    {
                        JObject source = (JObject)rectangle.DeepClone();
                        source["location"] = Point(120, 200);
                        source["boundingBox"] = new JArray(Point(120, 200), Point(122, 202));
                        actual.Add(Drawing(source, 203));
                    }
                }
            }
            return Success(actual);
        });

        await driver.DrawPageAsync(page, [page]);

        Assert.Equal(102, (int)Assert.Single(calls, call => call.Tool == "eplan_placement_delete").Args["items"]![0]!["oid"]!);
        JObject[] creations = calls.Where(call => call.Tool == "eplan_placement_create").SelectMany(call => ((JArray)call.Args["items"]!).OfType<JObject>()).ToArray();
        Assert.Equal(text["content"], Assert.Single(creations, item => (string?)item["kind"] == "text")["contents"]);
        Assert.Equal(new JArray(110d, 200d), Assert.Single(creations, item => (string?)item["kind"] == "text")["position"]);
        JObject grouped = Assert.Single(creations, item => (string?)item["kind"] == "block");
        Assert.Equal([101, 202, 203], ((JArray)grouped["members"]!).Select(item => (int)item["oid"]!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Math.PI / 2)]
    [InlineData(Math.PI)]
    public async Task LiveCircleAngles_ArePassedAsEqualDegrees(double angle)
    {
        JObject circle = new()
        {
            ["type"] = "Arc",
            ["oid"] = 1,
            ["kind"] = "Circle",
            ["center"] = Point(20, 30),
            ["radius"] = 3.5,
            ["startAngle"] = angle,
            ["endAngle"] = angle,
        };
        JObject item = await DrawCircuitGraphicAsync(circle);

        Assert.Equal("arc", (string?)item["kind"]);
        Assert.Equal(angle * 180 / Math.PI, (double)item["start_angle"]!, 10);
        Assert.Equal(item["start_angle"], item["end_angle"]);
    }

    [Theory]
    [InlineData("Rectangle")]
    [InlineData("Text")]
    [InlineData("PathText")]
    public async Task InheritedRotation_IsPassedUnchanged(string type)
    {
        JObject graphic = new()
        {
            ["type"] = type,
            ["oid"] = 1,
            ["angle"] = -16002,
            ["location"] = Point(20, 30),
            ["lowerLeft"] = Point(20, 30),
            ["upperRight"] = Point(22, 32),
            ["height"] = 2.5,
            ["content"] = new JObject { ["de_DE"] = "Beispiel" },
        };

        Assert.Equal(-16002, (double)(await DrawCircuitGraphicAsync(graphic))["angle"]!);
    }

    private static async Task<JObject> DrawCircuitGraphicAsync(JObject graphic)
    {
        JObject page = Page(graphic);
        page["pageType"] = "Circuit";
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (_, _) => Success([]));
        await driver.DrawPageAsync(page, [page]);
        return (JObject)Assert.Single(calls, call => call.Tool == "eplan_placement_create").Args["items"]![0]!;
    }

    private static SmallProjectRedrawDriver Driver(List<(string Tool, JObject Args)> calls, SmallProjectRedrawReport report,
        Func<string, JObject, JObject> respond) => new((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            return Task.FromResult(respond(tool, args));
        }, "ScratchTest", Path.GetTempPath(), report);

    private static JObject Success(IEnumerable<JObject> drawings) => new()
    {
        ["execution"] = "committed",
        ["drawings"] = new JObject { ["items"] = new JArray(drawings.Select(item => item.DeepClone())), ["truncated"] = false },
    };
    private static JObject Page(params JObject[] placements) => new() { ["name"] = "/1", ["pageType"] = "Overview", ["placements"] = new JArray(placements) };
    private static JObject Box() => new()
    {
        ["type"] = "PLC",
        ["oid"] = 1,
        ["name"] = "-Fixture",
        ["category"] = "PLCBox",
        ["location"] = Point(212, 284),
        ["parts"] = new JArray(new JObject { ["part"] = "PXC.2703994", ["variant"] = "1" }),
        ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = "PLCC", ["variant"] = 0 },
    };
    private static JObject Terminal(string macro) => new()
    {
        ["type"] = "Terminal",
        ["oid"] = 2,
        ["name"] = "-Fixture:1.1",
        ["category"] = "PLCTerminal",
        ["location"] = Point(238, 224),
        ["parentFunction"] = new JObject { ["oid"] = 1, ["name"] = "-Fixture" },
        ["properties"] = new JArray(Property(20468, macro)),
        ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = "PLCCPOV", ["variant"] = 3 },
    };
    private static JObject Property(int id, string value) => new() { ["id"] = id, ["index"] = 0, ["value"] = value };
    private static JObject Line(int oid, double x, double y) => new()
    {
        ["type"] = "Line",
        ["oid"] = oid,
        ["location"] = Point(x, y),
        ["start"] = Point(x, y),
        ["end"] = Point(x + 5, y),
        ["boundingBox"] = new JArray(Point(x, y), Point(x + 5, y)),
    };
    private static JObject Drawing(JObject source, int oid) => new()
    {
        ["kind"] = source["type"]!.DeepClone(),
        ["location"] = new JArray((double)source["location"]!["x"]!, (double)source["location"]!["y"]!),
        ["bounds"] = source["boundingBox"] is JArray bounds ? new JArray(bounds.SelectMany(point => new[] { (double)point["x"]!, (double)point["y"]! })) : null,
        ["locator"] = new JObject { ["type"] = 31, ["oid"] = oid, ["digest"] = "fixture" },
        ["properties"] = new JObject { ["name"] = source["name"]?.DeepClone(), ["grouped"] = "true", ["height_mm"] = "2.5", ["angle"] = "0" },
    };
    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
}
