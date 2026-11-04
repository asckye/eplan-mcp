using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawMacroTests
{
    [Fact]
    public async Task LiveControllerMacro_UsesReferencePointAndCoversNativeGraphics()
    {
        JObject box = Device(29038, "=K1+A2-KEB1", "Blackbox", 24, 100, "PXC\\2701290.ema", true);
        box["type"] = "BoxedDevice";
        box["parts"] = new JArray(new JObject { ["part"] = "PXC.2701290", ["variant"] = "1" });
        JObject terminal = Device(29040, "=K1+A2-KEB1:X3ETH", "DeviceEndTerminal", 108, 92, "PXC\\2701290.ema");
        JObject line = Line(123880, 20, 96);
        line["layer"] = 100;
        line["pen"] = new JObject { ["color"] = 7, ["width"] = 0.13 };
        JObject text = new()
        {
            ["type"] = "Text",
            ["oid"] = 9,
            ["location"] = Point(30, 90),
            ["height"] = 2.5,
            ["angle"] = 0,
            ["content"] = new JObject { ["de_DE"] = "USB" },
        };
        JObject page = Page("Circuit", box, terminal, line, text);
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        bool inserted = false;
        var driver = Driver(calls, report, (tool, _) =>
        {
            if (tool == "eplan_macro_list")
            { return Listed("PXC\\2701290.ema"); }
            if (tool == "eplan_macro_get")
            { return Described("MultiLine", Point(16, 168), MacroDevice(box, 8, 180), MacroDevice(terminal, 92, 172)); }
            if (tool == "eplan_macro_insert")
            { inserted = true; }
            JObject result = Success(inserted ? [Drawing(box, 1), Drawing(terminal, 2), Drawing(line, 3), Drawing(text, 9)] : []);
            result["devices"] = new JArray(new JObject { ["tag"] = "=K1+A2-KEB1", ["category"] = "Blackbox" });
            return result;
        });

        await driver.DrawPageAsync(page, [page]);

        JObject args = Assert.Single(calls, call => call.Tool == "eplan_macro_insert").Args;
        Assert.Equal("PXC/2701290.ema", (string?)args["macro"]);
        Assert.Equal("MultiLine", (string?)args["representation"]);
        Assert.Equal(0, (int)args["variant"]!);
        Assert.Equal(32d, (double)args["x"]!);
        Assert.Equal(88d, (double)args["y"]!);
        Assert.False((bool)args["macro_box"]!);
        Assert.DoesNotContain(calls, call => call.Tool is "eplan_device_place" or "eplan_placement_create");
        Assert.Equal("PXC.2701290", (string?)Assert.Single(calls, call => call.Tool == "eplan_device_update").Args["part"]);
    }

    [Fact]
    public async Task SameMacroOnSamePage_IsInsertedOnceForEachDeviceTag()
    {
        JObject first = Device(31617, "=F+A2-KE1:1.1", "PLCTerminal", 204, 92, "PXC\\2861344.ema");
        JObject second = Device(31628, "=K1+A2-KE4:1.1", "PLCTerminal", 304, 92, "PXC\\2861344.ema");
        JObject page = Page("Circuit", first, second);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (tool, _) =>
        {
            if (tool == "eplan_macro_list")
            { return Listed("PXC/2861344.ema"); }
            if (tool == "eplan_macro_get")
            { return Described("MultiLine", Point(0, 0), MacroDevice(first, 32, 80)); }
            JObject result = Success([]);
            result["devices"] = new JArray(new JObject { ["tag"] = "-Inserted" });
            return result;
        });

        await driver.DrawPageAsync(page, [page]);

        JObject[] insertions = calls.Where(call => call.Tool == "eplan_macro_insert").Select(call => call.Args).ToArray();
        Assert.Equal(2, insertions.Length);
        Assert.Equal([172d, 272d], insertions.Select(args => (double)args["x"]!));
        Assert.All(insertions, args => Assert.Equal(12d, (double)args["y"]!));
        Assert.Single(calls, call => call.Tool == "eplan_macro_get");
    }

    [Theory]
    [InlineData("Overview", true)]
    [InlineData("Circuit", false)]
    public async Task MissingControllerMacro_UsesExistingPartMacroAndReportsVersionDifferences(string pageType, bool partRoute)
    {
        JObject box = Device(29099, "=K1+A2-KE1", "PLCBox", 212, 284, "", true);
        box["type"] = "PLC";
        box["parts"] = new JArray(new JObject { ["part"] = "PXC.2703994", ["variant"] = "1" });
        JObject terminal = Device(29089, "=K1+A2-KE1:1.1", "PLCTerminal", 238, 276, "PXC\\2703981.ema");
        JObject unmatchedLine = Line(140001, 10, 40);
        JObject page = Page(pageType, box, terminal, unmatchedLine);
        page["size"] = new JObject { ["x"] = 420, ["y"] = 297 };
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = Driver(calls, report, (tool, args) =>
        {
            if (tool == "eplan_macro_list")
            { return Listed(null); }
            if (tool == "eplan_macro_insert" && (bool?)args["dry_run"] == true)
            { return new JObject { ["execution"] = "not_started", ["macro"] = "Custom/Controller-V2.ema" }; }
            if (tool == "eplan_macro_get")
            { return Described(pageType == "Overview" ? "Overview" : "MultiLine", Point(0, 292), MacroDevice(box, 212, 284), MacroDevice(terminal, 238, 276)); }
            JObject result = Success([]);
            result["devices"] = new JArray(new JObject { ["tag"] = "-Inserted", ["category"] = "PLCBox" });
            return result;
        });

        await driver.DrawPageAsync(page, [page]);

        JObject preview = Assert.Single(calls, call => call.Tool == "eplan_macro_insert" && (bool?)call.Args["dry_run"] == true).Args;
        Assert.Equal("PXC.2703994", (string?)preview["part"]);
        Assert.Equal(0d, (double)preview["x"]!);
        Assert.Equal(297d, (double)preview["y"]!);
        JObject args = Assert.Single(calls, call => call.Tool == "eplan_macro_insert" && (bool?)call.Args["dry_run"] == false).Args;
        Assert.Equal(partRoute ? "PXC.2703994" : null, (string?)args["part"]);
        Assert.Equal(partRoute ? null : "Custom/Controller-V2.ema", (string?)args["macro"]);
        Assert.DoesNotContain(calls, call => call.Tool is "eplan_device_place" or "eplan_placement_create");
        Assert.Equal(partRoute ? null : "PXC.2703994", (string?)Assert.Single(calls, call => call.Tool == "eplan_device_update").Args["part"]);
        Assert.NotEmpty((JArray)report.ToJson()["macroVersionDifferences"]!);
        Assert.Contains(((JArray)report.ToJson()["noToolTypes"]!).OfType<JObject>(), item => (string?)item["objectType"] == "Line"
            && ((JArray)item["objects"]!).Any(source => (string?)source["oid"] == "140001"));
    }

    [Fact]
    public async Task MissingReferencePoint_IsReportedWithoutGuessingInsertionPosition()
    {
        JObject box = Device(1, "-Fixture", "Blackbox", 24, 100, "PXC/2701290.ema", true);
        JObject page = Page("Circuit", box);
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = Driver(calls, report, (tool, _) => tool switch
        {
            "eplan_macro_list" => Listed("PXC/2701290.ema"),
            "eplan_macro_get" => Described("MultiLine", null, MacroDevice(box, 8, 180)),
            _ => Success([]),
        });

        await driver.DrawPageAsync(page, [page]);

        Assert.DoesNotContain(calls, call => call.Tool == "eplan_macro_insert");
        Assert.Contains(((JArray)report.ToJson()["warnings"]!).OfType<JObject>(), warning =>
            ((string?)warning["reason"])!.Contains("reference_point", StringComparison.Ordinal));
    }

    private static SmallProjectRedrawDriver Driver(List<(string Tool, JObject Args)> calls, SmallProjectRedrawReport report,
        Func<string, JObject, JObject> response) => new((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            return Task.FromResult(response(tool, args));
        }, "ScratchTest", Path.GetTempPath(), report);

    private static JObject Success(JObject[] drawings) => new()
    {
        ["execution"] = "committed",
        ["drawings"] = new JObject { ["items"] = new JArray(drawings), ["truncated"] = false },
    };
    private static JObject Listed(string? path) => new() { ["macros"] = path is null ? new JArray() : new JArray(new JObject { ["path"] = path }) };
    private static JObject Described(string representation, JObject? reference, params JObject[] devices) => new()
    {
        ["macro"] = new JObject
        {
            ["reference_point"] = reference,
            ["representations"] = new JArray(new JObject { ["type"] = representation, ["variants"] = new JArray(0) }),
            ["devices"] = new JArray(devices),
        },
    };
    private static JObject MacroDevice(JObject source, double x, double y) => new()
    {
        ["tag"] = source["name"]!.DeepClone(),
        ["category"] = source["category"]!.DeepClone(),
        ["main"] = source["isMain"]?.DeepClone(),
        ["location"] = Point(x, y),
    };
    private static JObject Page(string type, params JObject[] objects) => new() { ["name"] = "/1", ["pageType"] = type, ["placements"] = new JArray(objects) };
    private static JObject Device(int oid, string name, string category, double x, double y, string macro, bool main = false) => new()
    {
        ["type"] = category == "PLCTerminal" ? "Terminal" : "Function",
        ["oid"] = oid,
        ["name"] = name,
        ["category"] = category,
        ["isMain"] = main,
        ["location"] = Point(x, y),
        ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = "Fixture", ["variant"] = 0 },
        ["properties"] = new JArray(new JObject { ["id"] = 20468, ["index"] = 0, ["value"] = macro }),
    };
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
        ["location"] = source["location"]!.DeepClone(),
        ["symbol"] = source["symbol"]?.DeepClone(),
        ["bounds"] = source["boundingBox"] is JArray bounds ? new JArray(bounds.SelectMany(point => new[] { (double)point["x"]!, (double)point["y"]! })) : null,
        ["locator"] = new JObject { ["type"] = 31, ["oid"] = oid, ["digest"] = "fixture" },
        ["properties"] = new JObject
        {
            ["name"] = source["name"]?.DeepClone(),
            ["grouped"] = "true",
            ["height_mm"] = source["height"]?.ToString(),
            ["angle"] = source["angle"]?.ToString(),
        },
    };
    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
}
