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
        Assert.DoesNotContain(calls, call => call.Tool == "eplan_device_place" || call.Tool == "eplan_placement_create" && partRoute);
        Assert.Equal(partRoute ? null : "PXC.2703994", (string?)calls.Last(call => call.Tool == "eplan_device_update").Args["part"]);
        Assert.NotEmpty((JArray)report.ToJson()["macroVersionDifferences"]!);
        // A circuit page's own graphics are drawn; an Overview page keeps what its macro brought and lists the rest.
        if (partRoute)
        {
            Assert.Contains(((JArray)report.ToJson()["noToolTypes"]!).OfType<JObject>(), item => (string?)item["objectType"] == "Line"
                && ((JArray)item["objects"]!).Any(source => (string?)source["oid"] == "140001"));
        }
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

    [Fact]
    public async Task PlcModuleOnCircuit_ChoosesReproducingVariantAndJoinsTheCardTag()
    {
        // The sample's =K1+A2&EFS1/2 points of PXC.2861344: the macro holds points only, the card lives on an Overview page.
        JObject first = Device(1, "=K1+A2-KE4:1.1", "PLCTerminal", 304, 92, "PXC\\2861344.ema");
        JObject second = Device(2, "=K1+A2-KE4:1.2", "PLCTerminal", 312, 92, "PXC\\2861344.ema");
        JObject card = Device(3, "=K1+A2-KE4", "PLCBox", 212, 284, "", true);
        card["parts"] = new JArray(new JObject { ["part"] = "PXC.2861344", ["variant"] = "1" });
        JObject page = Page("Circuit", first, second);
        JObject overview = Page("Overview", card);
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        JObject Variant(double gap)
        {
            JObject described = Described("MultiLine", Point(32, 80), MacroDevice(first, 132, 80), MacroDevice(second, 132 + gap, 80));
            described["macro"]!["representations"]![0]!["variants"] = new JArray(0, 1);
            return described;
        }
        var driver = Driver(calls, report, (tool, args) =>
        {
            if (tool == "eplan_macro_list")
            { return Listed("PXC/2861344.ema"); }
            if (tool == "eplan_macro_get")
            { return Variant((int?)args["variant"] == 1 ? 8 : 20); }
            JObject result = Success([]);
            if (tool == "eplan_macro_insert")
            { result["devices"] = new JArray(new JObject { ["tag"] = "=K1+A2-A?:1.1" }, new JObject { ["tag"] = "=K1+A2-A?:1.2" }); }
            return result;
        });

        await driver.DrawPageAsync(page, [overview, page]);

        Assert.All(calls.Where(call => call.Tool == "eplan_macro_get"), call => Assert.Equal("MultiLine", (string?)call.Args["representation"]));
        JObject insert = Assert.Single(calls, call => call.Tool == "eplan_macro_insert").Args;
        Assert.Equal(1, (int)insert["variant"]!);
        Assert.Equal("keep", (string?)insert["numbering"]);
        Assert.Equal(204d, (double)insert["x"]!);
        Assert.Equal(92d, (double)insert["y"]!);
        JObject update = Assert.Single(calls, call => call.Tool == "eplan_device_update").Args;
        Assert.Equal("=K1+A2-A?", (string?)update["tag"]);
        Assert.Equal("=K1+A2-KE4", (string?)update["new_tag"]);
        Assert.Null(update["part"]);
    }

    [Fact]
    public async Task MovedPoint_DoesNotDecideTheVariantOrPosition()
    {
        // The sample's =K1+A2-KEB1: variant 1 fits, and its engineer moved X8 after inserting. Measured reference points.
        JObject box = Device(1, "=K1+A2-KEB1", "Blackbox", 24, 100, "PXC\\2701290.ema", true);
        JObject plus = Device(2, "=K1+A2-KEB1:+", "DeviceConnectionPoint", 32, 96, "PXC\\2701290.ema");
        JObject eth = Device(3, "=K1+A2-KEB1:X2ETH", "DeviceConnectionPoint", 88, 92, "PXC\\2701290.ema");
        JObject moved = Device(4, "=K1+A2-KEB1:X8", "DeviceConnectionPoint", 224, 93.1, "PXC\\2701290.ema");
        JObject page = Page("Circuit", box, plus, eth, moved);
        JObject Variant(double[] reference, double boxY, double plusY, double ethY, double movedY)
        {
            JObject described = Described("MultiLine", Point(reference[0], reference[1]), MacroDevice(box, 8, boxY), MacroDevice(plus, 16, plusY), MacroDevice(eth, 72, ethY), MacroDevice(moved, 208, movedY));
            described["macro"]!["representations"]![0]!["variants"] = new JArray(0, 1);
            return described;
        }
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (tool, args) => tool switch
        {
            "eplan_macro_list" => Listed("PXC/2701290.ema"),
            "eplan_macro_get" => (int?)args["variant"] == 1 ? Variant([16, 128], 132, 128, 124, 120) : Variant([16, 168], 208, 168, 172, 176),
            _ => Success([]),
        });

        await driver.DrawPageAsync(page, [page]);

        JObject insert = Assert.Single(calls, call => call.Tool == "eplan_macro_insert").Args;
        Assert.Equal(1, (int)insert["variant"]!);
        Assert.Equal(32d, (double)insert["x"]!);
        Assert.Equal(96d, (double)insert["y"]!);
    }

    [Fact]
    public async Task DeviceWithPart_ComesFromTheLibraryWithTheSourceSymbol()
    {
        JObject device = Device(5, "=K1+A2-FC2", "CircuitBreaker", 112, 248, "", true);
        device["symbol"] = new JObject { ["library"] = "IEC_symbol", ["name"] = "FAH1", ["variant"] = 0 };
        device["parts"] = new JArray(new JObject { ["part"] = "SIE.5SY4106-7", ["variant"] = "1" });
        JObject page = Page("Circuit", device);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (_, _) => Success([]));

        await driver.DrawPageAsync(page, [page]);

        JObject place = Assert.Single(calls, call => call.Tool == "eplan_device_place").Args;
        Assert.Equal(("FAH1", "SIE.5SY4106-7"), ((string?)place["symbol"], (string?)place["part"]));
        Assert.DoesNotContain(calls, call => call.Tool == "eplan_device_update");
    }

    [Fact]
    public async Task PartTheLibraryCannotPlace_IsDrawnWithItsSymbol()
    {
        JObject device = Device(5, "=K1+A2-FC2", "CircuitBreaker", 112, 248, "", true);
        device["symbol"] = new JObject { ["library"] = "IEC_symbol", ["name"] = "FAH1", ["variant"] = 0 };
        device["parts"] = new JArray(new JObject { ["part"] = "RIT.3581000", ["variant"] = "1" });
        JObject page = Page("Circuit", device);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (tool, args) =>
            tool == "eplan_device_place" && args["part"] is not null ? JObject.Parse("""{"error":{"reason":"EPLAN_OPERATION_FAILED","message":"EPLAN could not place part","execution":"rolled_back"}}""") : Success([]));

        await driver.DrawPageAsync(page, [page]);

        JObject[] places = [.. calls.Where(call => call.Tool == "eplan_device_place").Select(call => call.Args)];
        Assert.Equal(2, places.Length);
        Assert.Null(places[1]["part"]);
        JObject[] updates = [.. calls.Where(call => call.Tool == "eplan_device_update").Select(call => call.Args)];
        Assert.Equal("RIT.3581000", (string?)updates[0]["part"]);
        Assert.True((bool?)updates[1]["update_from_parts"]);
    }

    [Fact]
    public async Task PlcData_GoesToTheCardAndThePointsByExactLocator()
    {
        JObject card = Device(1, "=K1+A2-KE4", "PLCBox", 212, 284, "", true);
        card["type"] = "PLC";
        card["properties"] = new JArray(new JObject { ["id"] = 20161, ["index"] = 0, ["value"] = "Phoenix1" }, new JObject { ["id"] = 20308, ["index"] = 0, ["value"] = "6" });
        JObject point = Device(2, "=K1+A2-KE4:1.1", "PLCTerminal", 238, 276, "");
        point["properties"] = new JArray(new JObject { ["id"] = 20400, ["index"] = 0, ["value"] = "%IX2.4" },
            new JObject { ["id"] = 20011, ["index"] = 0, ["value"] = new JObject { ["en_US"] = "Voltage", ["??_??"] = "Independent" } });
        JObject page = Page("Circuit", card, point);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (tool, _) =>
            tool == "eplan_page_get" ? Success([Drawing(card, 11), Drawing(point, 12)]) : Success([]));

        await driver.DrawPageAsync(page, [page]);

        JArray changes = (JArray)Assert.Single(calls, call => call.Tool == "eplan_property_set").Args["changes"]!;
        Assert.Equal(["20161|Phoenix1", "20308|6", "20400|%IX2.4", "20011|Voltage", "20011|Independent"], changes.Select(c => c["property"]!["property_id"] + "|" + c["value"]));
        Assert.Equal(6, (int)changes[1]["value"]!);
        Assert.Equal("en_US", (string?)changes[3]["language"]);
        Assert.Equal("function", (string?)changes[2]["target"]!["locator"]!["kind"]);
    }

    [Fact]
    public async Task PartMark_IsNeverDrawnByHand()
    {
        JObject mark = new()
        {
            ["type"] = "PolyLine",
            ["oid"] = 7,
            ["closed"] = true,
            ["location"] = Point(26, 57),
            ["points"] = new JArray(Point(26, 57), Point(27, 57), Point(27, 58)),
            ["properties"] = new JArray(new JObject { ["id"] = 19101, ["index"] = 0, ["value"] = "0" }),
        };
        JObject page = Page("Circuit", mark);
        var calls = new List<(string Tool, JObject Args)>();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = Driver(calls, report, (_, _) => Success([]));

        await driver.DrawPageAsync(page, [page]);

        Assert.DoesNotContain(calls, call => call.Tool == "eplan_placement_create");
        Assert.NotEmpty((JArray)report.ToJson()["macroVersionDifferences"]!);
    }

    [Fact]
    public async Task ReportSpanningPages_IsGeneratedOnceFromItsFirstPage()
    {
        // The sample's PLC diagrams: one report with one page per card, not one report per page.
        static JObject Report(string name) => new()
        {
            ["name"] = name,
            ["pageType"] = "PLCDiagram",
            ["placements"] = new JArray(),
            ["report"] = new JArray(new JObject { ["type"] = "PLCDiagram", ["formName"] = "F19_001", ["deviceTag"] = name }),
        };
        JObject first = Report("/6"), second = Report("/7");
        var calls = new List<(string Tool, JObject Args)>();
        var driver = Driver(calls, new SmallProjectRedrawReport("test", "ScratchTest"), (_, _) => Success([]));

        await driver.DrawPageAsync(first, [first, second]);
        await driver.DrawPageAsync(second, [first, second]);

        Assert.Equal("/6", (string?)Assert.Single(calls, call => call.Tool == "eplan_report_create").Args["first_page"]);
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
