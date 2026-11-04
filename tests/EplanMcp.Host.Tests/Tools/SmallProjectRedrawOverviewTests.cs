using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawOverviewTests
{
    [Fact]
    public async Task Overview_InsertsWithoutMacroBoxThenRenamesPlcAndVerifiesSuffixFunctions()
    {
        JObject box = new()
        {
            ["type"] = "PLC",
            ["oid"] = 1,
            ["name"] = "-NEW",
            ["category"] = "PLCBox",
            ["location"] = Point(212, 284),
            ["parts"] = new JArray(new JObject { ["part"] = "FICTION.PLC", ["variant"] = "1" }),
            ["properties"] = new JArray(new JObject { ["id"] = 20468, ["value"] = "FICTION/PLC.ema" }),
            ["symbol"] = new JObject { ["name"] = "PLCC", ["library"] = "SPECIAL", ["variant"] = 0 },
        };
        JObject point = new()
        {
            ["type"] = "Terminal",
            ["oid"] = 2,
            ["name"] = "-NEW:3.1",
            ["category"] = "PLCTerminal",
            ["location"] = Point(220, 280),
            ["parentFunction"] = new JObject { ["oid"] = 1, ["name"] = "-NEW" },
        };
        JObject source = Page(box, point);
        var calls = new List<(string Tool, JObject Args)>();
        bool renamed = false;
        var driver = new SmallProjectRedrawDriver((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            if (tool == "eplan_device_update")
            { renamed = true; }
            if (tool == "eplan_macro_list")
            { return Task.FromResult(new JObject { ["macros"] = new JArray(new JObject { ["path"] = "FICTION/PLC.ema" }) }); }
            if (tool == "eplan_macro_get")
            {
                return Task.FromResult(new JObject
                {
                    ["macro"] = new JObject
                    {
                        ["reference_point"] = Point(0, 292),
                        ["representations"] = new JArray(new JObject { ["type"] = "Overview", ["variants"] = new JArray(0) }),
                        ["devices"] = new JArray(
                            new JObject { ["tag"] = "-OLD", ["category"] = "PLCBox", ["main"] = true, ["location"] = Point(212, 284) },
                            new JObject { ["tag"] = "-OLD:3.1", ["category"] = "PLCTerminal", ["location"] = Point(220, 280) }),
                    },
                });
            }
            JObject actualBox = Drawing(box, 101), actualPoint = Drawing(point, 102);
            actualBox["properties"]!["name"] = renamed ? "-NEW" : "-OLD";
            actualPoint["properties"]!["name"] = renamed ? "-NEW:3.1" : "-OLD:3.1";
            return Task.FromResult(new JObject
            {
                ["execution"] = "committed",
                ["devices"] = new JArray(new JObject { ["tag"] = "-OLD", ["category"] = "PLCBox" }),
                ["drawings"] = new JObject { ["items"] = renamed ? new JArray(actualBox, actualPoint) : new JArray(), ["truncated"] = false },
            });
        }, "ScratchTest", Path.GetTempPath(), new SmallProjectRedrawReport("test", "ScratchTest"));

        await driver.DrawPageAsync(source, [source]);

        Assert.False((bool)Assert.Single(calls, c => c.Tool == "eplan_macro_insert").Args["macro_box"]!);
        JObject rename = Assert.Single(calls, c => c.Tool == "eplan_device_update").Args;
        Assert.Equal("-OLD", (string?)rename["tag"]);
        Assert.Equal("-NEW", (string?)rename["new_tag"]);
        Assert.True(calls.FindIndex(c => c.Tool == "eplan_device_update") < calls.FindLastIndex(c => c.Tool == "eplan_page_get"));
        Assert.DoesNotContain(calls, c => c.Tool == "eplan_device_place");
    }

    [Fact]
    public async Task Overview_ReconcilesAllPagesByGeometryAndRebuildsMacroText()
    {
        JObject sourceLine = Line(1, 10), missingLine = Line(2, 30), sourceText = Text(3, 50);
        JObject source = Page(sourceLine, missingLine, sourceText);
        JObject existing = Drawing(sourceLine, 101), extra = Drawing(Line(4, 70), 104), macroText = Drawing(sourceText, 103);
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = Driver(calls, report, () => [existing, extra, macroText]);

        await driver.DrawPageAsync(source, [source]);

        JObject deletion = Assert.Single(calls, c => c.Tool == "eplan_placement_delete").Args;
        Assert.Equal([104, 103], ((JArray)deletion["items"]!).Select(i => (int)i["oid"]!));
        Assert.False((bool)deletion["dry_run"]!);
        JObject creation = Assert.Single(calls, c => c.Tool == "eplan_placement_create").Args;
        Assert.Equal("warn", (string?)creation["layout_guard"]);
        JObject[] items = ((JArray)creation["items"]!).OfType<JObject>().ToArray();
        Assert.Equal(["line", "text"], items.Select(i => (string?)i["kind"]));
        Assert.Equal(sourceText["content"], items[1]["contents"]);
        Assert.Contains(((JArray)report.ToJson()["notes"]!).OfType<JObject>(), n => ((string?)n["reason"])!.Contains("retained", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.009, false)]
    [InlineData(0.011, true)]
    public async Task Overview_GeometryToleranceIsOneHundredthMillimetre(double offset, bool replaces)
    {
        JObject line = Line(1, 10), existing = Drawing(line, 101);
        existing["location"]![0] = 10 + offset;
        var calls = new List<(string Tool, JObject Args)>();
        JObject source = Page(line);
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), () => [existing]);

        await driver.DrawPageAsync(source, [source]);

        Assert.Equal(replaces, calls.Any(c => c.Tool == "eplan_placement_delete"));
        Assert.Equal(replaces, calls.Any(c => c.Tool == "eplan_placement_create"));
    }

    [Fact]
    public async Task Overview_PenWidthDifferenceBelowPositionToleranceStillReplacesGraphic()
    {
        JObject line = Line(1, 10), existing = Drawing(line, 101);
        line["pen"] = new JObject { ["width"] = 0.25 };
        existing["properties"]!["pen_width_mm"] = "0.255";
        var calls = new List<(string Tool, JObject Args)>();
        JObject source = Page(line);
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), () => [existing]);

        await driver.DrawPageAsync(source, [source]);

        Assert.Single(calls, c => c.Tool == "eplan_placement_delete");
        JObject item = (JObject)Assert.Single((JArray)Assert.Single(calls, c => c.Tool == "eplan_placement_create").Args["items"]!);
        Assert.Equal(0.25, (double)item["pen"]!["width_mm"]!);
    }

    [Fact]
    public async Task Overview_BuildsMissingNamedBlockFromAbsoluteMemberGeometry()
    {
        JObject first = Line(1, 0), second = Line(1, 10);
        JObject block = new() { ["type"] = "Block", ["oid"] = 5, ["name"] = "144_268557", ["location"] = Point(100, 200), ["members"] = new JArray(first, second) };
        JObject source = Page(block);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), () => [Drawing(Line(1, 100, 220), 101), Drawing(Line(2, 110, 220), 102)]);

        await driver.DrawPageAsync(source, [source]);

        JObject creation = Assert.Single(calls, c => c.Tool == "eplan_placement_create").Args;
        JObject item = (JObject)Assert.Single((JArray)creation["items"]!);
        Assert.Equal("block", (string?)item["kind"]);
        Assert.Equal("144_268557", (string?)item["name"]);
        Assert.Equal([101, 102], ((JArray)item["members"]!).Select(i => (int)i["oid"]!));
        Assert.DoesNotContain(calls, c => c.Tool == "eplan_placement_delete");
    }

    [Fact]
    public async Task Overview_DoesNotReplayTopLevelCopiesOfBlockMembers()
    {
        JObject first = Line(1, 0), second = Line(2, 10);
        JObject block = new()
        {
            ["type"] = "Block",
            ["oid"] = 5,
            ["name"] = "MemberCopies",
            ["location"] = Point(100, 200),
            ["memberOids"] = new JArray(1, 2),
            ["members"] = new JArray(first, second),
        };
        JObject source = Page(Line(1, 100, 220), Line(2, 110, 220), block);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), () => [Drawing(Line(1, 100, 220), 101), Drawing(Line(2, 110, 220), 102)]);

        await driver.DrawPageAsync(source, [source]);

        JObject item = (JObject)Assert.Single((JArray)Assert.Single(calls, c => c.Tool == "eplan_placement_create").Args["items"]!);
        Assert.Equal("block", (string?)item["kind"]);
        Assert.Equal(2, ((JArray)item["members"]!).Count);
        Assert.DoesNotContain(calls, c => c.Tool == "eplan_placement_delete");
    }

    [Fact]
    public async Task Overview_ConvertsEveryBlockMemberCoordinateBeforeCreatingMissingGraphics()
    {
        JObject rectangle = new()
        {
            ["type"] = "Rectangle",
            ["oid"] = 2,
            ["location"] = Point(0, 0),
            ["lowerLeft"] = Point(0, 0),
            ["upperRight"] = Point(5, 8),
            ["angle"] = Math.PI / 2,
        };
        JObject arc = new()
        {
            ["type"] = "Arc",
            ["oid"] = 3,
            ["location"] = Point(2, 4),
            ["center"] = Point(2, 4),
            ["kind"] = "Circle",
            ["radius"] = 2,
            ["startAngle"] = 0,
            ["endAngle"] = 0,
        };
        JObject polyline = new()
        {
            ["type"] = "PolyLine",
            ["oid"] = 4,
            ["location"] = Point(-1, 0),
            ["closed"] = true,
            ["points"] = new JArray(Point(-1, 0), Point(1, 2)),
        };
        JObject text = Text(5, -2);
        text["location"] = Point(-2, -4);
        JObject block = new()
        {
            ["type"] = "Block",
            ["oid"] = 6,
            ["name"] = "Geometry",
            ["location"] = Point(100, 200),
            ["members"] = new JArray(Line(1, 0), rectangle, arc, polyline, text),
        };
        JObject source = Page(block);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), () => []);

        await driver.DrawPageAsync(source, [source]);

        JObject[] items = ((JArray)Assert.Single(calls, c => c.Tool == "eplan_placement_create").Args["items"]!).OfType<JObject>().ToArray();
        Assert.Equal(new JArray(new JArray(100d, 220d), new JArray(105d, 220d)), items[0]["points"]);
        Assert.Equal(new JArray(new JArray(100d, 200d), new JArray(105d, 208d)), items[1]["corners"]);
        Assert.Equal(90, (double)items[1]["angle"]!, 10);
        Assert.Equal(new JArray(102d, 204d), items[2]["center"]);
        Assert.Equal(new JArray(new JArray(99d, 200d), new JArray(101d, 202d)), items[3]["points"]);
        Assert.True((bool)items[3]["closed"]!);
        Assert.Equal(new JArray(98d, 196d), items[4]["position"]);
        Assert.Equal(text["content"], items[4]["contents"]);
    }

    [Fact]
    public async Task Overview_BlockCanContainExistingDeviceAlongsideGraphics()
    {
        JObject device = new() { ["type"] = "PLC", ["oid"] = 1, ["name"] = "-CARD", ["location"] = Point(0, 20) };
        JObject line = Line(2, 10);
        JObject block = new() { ["type"] = "Block", ["oid"] = 5, ["name"] = "MixedMembers", ["location"] = Point(100, 200), ["members"] = new JArray(device, line) };
        JObject actualDevice = new()
        {
            ["kind"] = "PLC",
            ["location"] = new JArray(100d, 220d),
            ["locator"] = new JObject { ["type"] = 14, ["oid"] = 101 },
            ["properties"] = new JObject { ["name"] = "-CARD", ["grouped"] = "false" },
        };
        JObject source = Page(block);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), () => [actualDevice, Drawing(Line(2, 110, 220), 102)]);

        await driver.DrawPageAsync(source, [source]);

        JObject item = (JObject)Assert.Single((JArray)Assert.Single(calls, c => c.Tool == "eplan_placement_create").Args["items"]!);
        Assert.Equal("block", (string?)item["kind"]);
        Assert.Equal([101, 102], ((JArray)item["members"]!).Select(member => (int)member["oid"]!));
        Assert.DoesNotContain(calls, c => c.Tool == "eplan_placement_delete");
    }

    [Fact]
    public async Task Overview_DoesNotBuildPartialBlockAndReportsMissingMember()
    {
        JObject block = new() { ["type"] = "Block", ["oid"] = 5, ["name"] = "MissingMember", ["location"] = Point(100, 200), ["members"] = new JArray(Line(1, 0), Line(2, 10)) };
        JObject source = Page(block);
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = Driver(calls, report, () => [Drawing(Line(1, 100, 220), 101)]);

        await driver.DrawPageAsync(source, [source]);

        Assert.DoesNotContain(calls, c => c.Tool == "eplan_placement_create" && (string?)c.Args["items"]?[0]?["kind"] == "block");
        Assert.Contains(((JArray)report.ToJson()["warnings"]!).OfType<JObject>(), w => ((string?)w["reason"])!.Contains("block was not created", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("PathText")]
    public async Task Overview_MissingBlockMembersDoNotDuplicateExistingMacroText(string type)
    {
        JObject text = Text(1, 20);
        text["type"] = type;
        JObject missingLine = Line(2, 40);
        JObject block = new() { ["type"] = "Block", ["oid"] = 3, ["name"] = "UnreadableMembers", ["location"] = Point(0, 0) };
        JObject source = Page(text, missingLine, block);
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = Driver(calls, report, () => [Drawing(text, 101)]);

        await driver.DrawPageAsync(source, [source]);

        Assert.DoesNotContain(calls, c => c.Tool == "eplan_placement_delete");
        JObject item = (JObject)Assert.Single((JArray)Assert.Single(calls, c => c.Tool == "eplan_placement_create").Args["items"]!);
        Assert.Equal("line", (string?)item["kind"]);
        Assert.Contains(((JArray)report.ToJson()["warnings"]!).OfType<JObject>(), w => ((string?)w["reason"])!.Contains("avoid duplicate text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Overview_FailedTextDeletionDoesNotGroupSurvivingMacroText()
    {
        JObject block = new()
        {
            ["type"] = "Block",
            ["oid"] = 3,
            ["name"] = "TextBlock",
            ["location"] = Point(100, 200),
            ["members"] = new JArray(Line(1, 0), Text(2, 0)),
        };
        JObject actualText = Drawing(Text(2, 100), 102);
        actualText["location"] = new JArray(100d, 220d);
        actualText["properties"]!["text"] = "de_DE@Beispiel;";
        JObject source = Page(block);
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = new SmallProjectRedrawDriver((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            return Task.FromResult(tool == "eplan_placement_delete"
                ? new JObject { ["error"] = new JObject { ["reason"] = "PLAN_STALE", ["execution"] = "not_started" } }
                : new JObject
                {
                    ["execution"] = "committed",
                    ["drawings"] = new JObject { ["items"] = new JArray(Drawing(Line(1, 100, 220), 101), actualText.DeepClone()), ["truncated"] = false },
                });
        }, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        Assert.Single(calls, c => c.Tool == "eplan_placement_delete");
        Assert.DoesNotContain(calls, c => c.Tool == "eplan_placement_create");
        Assert.Contains(((JArray)report.ToJson()["warnings"]!).OfType<JObject>(), w => ((string?)w["reason"])!.Contains("this missing block was not created", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Overview_IncompleteSourcePreventsGroupingOtherBlocksAroundUnreplacedText()
    {
        JObject unreadable = new() { ["type"] = "Block", ["oid"] = 3, ["name"] = "Unreadable", ["location"] = Point(0, 0) };
        JObject complete = new()
        {
            ["type"] = "Block",
            ["oid"] = 4,
            ["name"] = "TextBlock",
            ["location"] = Point(100, 200),
            ["members"] = new JArray(Line(1, 0), Text(2, 0)),
        };
        JObject actualText = Drawing(Text(2, 100), 102);
        actualText["location"] = new JArray(100d, 220d);
        JObject source = Page(unreadable, complete);
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = Driver(calls, report, () => [Drawing(Line(1, 100, 220), 101), actualText]);

        await driver.DrawPageAsync(source, [source]);

        Assert.DoesNotContain(calls, c => c.Tool is "eplan_placement_create" or "eplan_placement_delete");
        Assert.Equal(2, ((JArray)report.ToJson()["warnings"]!).OfType<JObject>()
            .Count(w => ((string?)w["reason"])!.Contains("this missing block was not created", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Overview_LeavesBlockMembersAndAlreadyNamedBlocksUntouched()
    {
        JObject line = Drawing(Line(1, 10), 101);
        line["properties"]!["grouped"] = "true";
        line["parent"] = new JObject { ["type"] = 144, ["oid"] = 5 };
        JObject block = new() { ["type"] = "Block", ["oid"] = 5, ["name"] = "Existing", ["location"] = Point(0, 0), ["members"] = new JArray() };
        JObject actualBlock = new() { ["kind"] = "Block", ["properties"] = new JObject { ["name"] = "Existing" } };
        JObject source = Page(block);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), () => [line, actualBlock]);

        await driver.DrawPageAsync(source, [source]);

        Assert.DoesNotContain(calls, c => c.Tool is "eplan_placement_create" or "eplan_placement_delete");
    }

    private static SmallProjectRedrawDriver Driver(List<(string Tool, JObject Args)> calls, SmallProjectRedrawReport report, Func<JObject[]> drawings) => new((tool, args) =>
    {
        calls.Add((tool, (JObject)args.DeepClone()));
        return Task.FromResult(new JObject
        {
            ["execution"] = "committed",
            ["drawings"] = new JObject { ["items"] = new JArray(drawings().Select(d => d.DeepClone())), ["truncated"] = false },
        });
    }, "ScratchTest", Path.GetTempPath(), report);

    private static JObject Page(params JObject[] placements) => new() { ["name"] = "/1", ["pageType"] = "Overview", ["placements"] = new JArray(placements), ["connections"] = new JArray() };
    private static JObject Line(int oid, double x, double y = 20) => new()
    {
        ["type"] = "Line",
        ["oid"] = oid,
        ["location"] = Point(x, y),
        ["start"] = Point(x, y),
        ["end"] = Point(x + 5, y),
        ["boundingBox"] = new JArray(Point(x, y), Point(x + 5, y)),
    };
    private static JObject Text(int oid, double x) => new()
    {
        ["type"] = "Text",
        ["oid"] = oid,
        ["location"] = Point(x, 20),
        ["height"] = 2.5,
        ["angle"] = 0,
        ["content"] = new JObject { ["en_US"] = "Example", ["de_DE"] = "Beispiel", ["zh_CN"] = "示例" },
    };
    private static JObject Drawing(JObject source, int oid) => new()
    {
        ["kind"] = source["type"]!.DeepClone(),
        ["location"] = new JArray((double)source["location"]!["x"]!, (double)source["location"]!["y"]!),
        ["bounds"] = source["boundingBox"] is JArray bounds ? new JArray(bounds.SelectMany(p => new[] { (double)p["x"]!, (double)p["y"]! })) : null,
        ["locator"] = new JObject { ["type"] = 31, ["oid"] = oid, ["digest"] = "fixture" },
        ["properties"] = new JObject { ["grouped"] = "false", ["height_mm"] = "2.5", ["angle"] = "0" },
    };
    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
}
