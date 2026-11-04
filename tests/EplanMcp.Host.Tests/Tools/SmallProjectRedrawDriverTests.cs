using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawDriverTests
{
    [Fact]
    public async Task Pages_AllCircuitAndOverviewPagesExistBeforeDrawingStarts()
    {
        JObject first = Page(Device(1, "Function", "-K1", "K", 10));
        JObject second = Page();
        second["name"] = "/2";
        second["pageType"] = "Overview";
        JObject reportPage = Page();
        reportPage["name"] = "/3";
        reportPage["isReportPage"] = true;
        var calls = new List<(string Tool, JObject Args)>();
        var driver = new SmallProjectRedrawDriver((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            return Task.FromResult(Success(args));
        }, "ScratchTest", Path.GetTempPath(), new SmallProjectRedrawReport("test", "ScratchTest"));

        await driver.DrawPageAsync(first, [first, second, reportPage]);
        await driver.DrawPageAsync(second, [first, second, reportPage]);

        Assert.Equal(["/1", "/2"], calls.Where(call => call.Tool == "eplan_page_create")
            .Select(call => (string?)call.Args["pages"]![0]!["name"]));
        Assert.Equal(["eplan_page_create", "eplan_page_create", "eplan_page_open"], calls.Take(3).Select(call => call.Tool));
        Assert.Single(calls, call => call.Tool == "eplan_device_place");
    }

    [Fact]
    public async Task Overview_UsesFixedPositionAndReportsActualPlcBoxDisplacement()
    {
        JObject box = Device(1, "PLC", "-K1", "PLCC", 212);
        box["location"] = Point(212, 284);
        box["category"] = "PLCBox";
        box["parts"] = new JArray(new JObject { ["part"] = "FICTION.PLC", ["variant"] = "1" });
        box["boundingBox"] = new JArray(Point(212, 20), Point(244, 284));
        JObject property = new()
        {
            ["type"] = "ContextPropertyPlacement",
            ["oid"] = 2,
            ["boundingBox"] = new JArray(Point(-204, -200), Point(-200, -190)),
        };
        JObject point = Device(3, "Terminal", "-K1:1:2", "DCP", 216);
        point["category"] = "PLCTerminal";
        point["parentFunction"] = new JObject { ["oid"] = 1, ["name"] = "-K1" };
        JObject source = Page(box, property, point);
        source["pageType"] = "Overview";
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        int reads = 0;
        var driver = new SmallProjectRedrawDriver((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            JObject result = Success(args);
            if (tool == "eplan_macro_insert")
            { result["devices"] = new JArray(new JObject { ["tag"] = "-K1", ["category"] = "PLCBox" }); }
            if (tool == "eplan_page_get" && ++reads > 1)
            {
                JObject actual = Drawing(box, 101);
                actual["location"] = new JArray(212, 288);
                result["drawings"]!["items"] = new JArray(actual);
            }
            return Task.FromResult(result);
        }, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        JObject macro = Assert.Single(calls, call => call.Tool == "eplan_macro_insert").Args;
        Assert.Equal((0d, 292d), ((double)macro["x"]!, (double)macro["y"]!));
        Assert.DoesNotContain(calls, call => call.Tool == "eplan_macro_get");
        Assert.DoesNotContain(calls, call => call.Tool == "eplan_device_place" && (string?)call.Args["symbol"] == "DCP");
        Assert.Contains(((JArray)report.ToJson()["notes"]!).OfType<JObject>(),
            note => ((string?)note["reason"])?.Contains("dx=0; dy=4", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Interruptions_ResolveReferenceAndSameNamePagesWithoutSendingEmptyCounterparts()
    {
        JObject point = Device(1, "InterruptionPoint", "-2L1", "BP", 376);
        point["direction"] = "Left";
        point["counterpartReference"] = "-FC2:1";
        JObject source = Page(point);
        JObject sameName = Page(Device(2, "InterruptionPoint", "-2L1", "BP", 48));
        sameName["name"] = "/2";
        JObject referenced = Page(Device(3, "Function", "-FC2", "K", 20));
        referenced["name"] = "/3";
        JObject orphan = Device(4, "InterruptionPoint", "-ORPHAN", "BP", 376);
        orphan["direction"] = "Left";
        orphan["counterpartReference"] = "-ABSENT:1";
        ((JArray)source["placements"]!).Add(orphan);
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        var driver = new SmallProjectRedrawDriver((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            return Task.FromResult(Success(args));
        }, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source, sameName, referenced]);

        JObject connection = Assert.Single(calls, call => call.Tool == "eplan_connection_create").Args;
        Assert.Equal(["/2", "/3"], connection["items"]![0]!["counterpart_pages"]!.Values<string>());
        Assert.Contains(((JArray)report.ToJson()["notes"]!).OfType<JObject>(),
            note => (string?)note["oid"] == "4" && ((string?)note["reason"])?.Contains("not placed", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Devices_OptionalPlcFieldsAreOmittedAndLaterFunctionsJoinExistingTag()
    {
        JObject first = Device(1, "PLC", "-K1", "PLCC", 10);
        JObject later = Device(2, "Function", "-K1", "K", 30);
        JObject point = Device(3, "Terminal", "-K1:1:2", "DCP", 40);
        point["category"] = "PLCTerminal";
        point["parentFunction"] = new JObject { ["name"] = "-K1" };
        point["properties"] = new JArray(new JObject { ["id"] = 20400, ["index"] = 0, ["value"] = "I0.1" },
            new JObject { ["id"] = 20402, ["index"] = 0, ["value"] = "" });
        JObject source = Page(first, later, point);
        var calls = new List<(string Tool, JObject Args)>();
        var driver = new SmallProjectRedrawDriver((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            return Task.FromResult(Success(args));
        }, "ScratchTest", Path.GetTempPath(), new SmallProjectRedrawReport("test", "ScratchTest"));

        await driver.DrawPageAsync(source, [source]);

        JObject[] placed = calls.Where(call => call.Tool == "eplan_device_place").Select(call => call.Args).ToArray();
        Assert.Equal(3, placed.Length);
        Assert.Null(placed[0]["join_device"]);
        Assert.True((bool)placed[1]["join_device"]!);
        Assert.Null(placed[2]["join_device"]);
        Assert.Equal("I0.1", (string?)placed[2]["plc_address"]);
        Assert.Null(placed[2]["plc_connection_point"]);
        Assert.Null(placed[2]["plc_symbolic_address"]);
        Assert.Null(placed[2]["plc_text"]);
        Assert.Null(placed[2]["plc_plug_designation"]);
    }

    [Fact]
    public async Task Definitions_PreserveCommaCrossSectionAndReportPartOnlyPoints()
    {
        JObject point = Device(1, "ConnectionDefinitionPoint", "", "CDP", 200);
        point["properties"] = new JArray(new JObject { ["id"] = 31002, ["index"] = 0, ["value"] = "1,5" });
        JObject partOnly = Device(2, "ConnectionDefinitionPoint", "", "CDP", 204);
        partOnly["parts"] = new JArray(new JObject { ["part"] = "FICTION.PART" });
        JObject source = Page(point, partOnly);
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        var driver = new SmallProjectRedrawDriver((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            return Task.FromResult(Success(args));
        }, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        JObject connection = Assert.Single(calls, call => call.Tool == "eplan_connection_create").Args;
        JToken definition = Assert.Single((JArray)connection["items"]!);
        Assert.Equal("1,5", (string?)definition["cross_section"]);
        Assert.Contains(((JArray)report.ToJson()["notes"]!).OfType<JObject>(),
            note => (string?)note["oid"] == "2" && ((string?)note["reason"])?.Contains("part references alone", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Definitions_InvalidPositionBatchRetriesEachPointOnce()
    {
        JObject valid = Device(1, "ConnectionDefinitionPoint", "", "CDP", 200);
        JObject invalid = Device(2, "ConnectionDefinitionPoint", "", "CDP", 204);
        valid["properties"] = new JArray(new JObject { ["id"] = 31002, ["index"] = 0, ["value"] = "1,5" });
        invalid["properties"] = valid["properties"]!.DeepClone();
        JObject source = Page(valid, invalid);
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        var driver = new SmallProjectRedrawDriver((tool, args) =>
        {
            calls.Add((tool, (JObject)args.DeepClone()));
            bool invalidPoint = tool == "eplan_connection_create" && ((JArray)args["items"]!).Any(item => (double)item["position"]![0]! == 204);
            return Task.FromResult(invalidPoint ? new JObject
            {
                ["error"] = new JObject
                {
                    ["reason"] = "INVALID_ARGUMENT",
                    ["execution"] = "not_started",
                    ["message"] = "Invalid arguments: /items/0/position: Place the definition point on a segment of this connection. Nearest existing segment: [[200.0,219.0],[200.0,229.0]].",
                },
            } : Success(args));
        }, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        Assert.Equal([2, 1, 1], calls.Where(call => call.Tool == "eplan_connection_create").Select(call => ((JArray)call.Args["items"]!).Count));
        Assert.Equal(2, (int)report.ToJson()["failedToolCallCount"]!);
        Assert.Contains(((JArray)report.ToJson()["calls"]!).OfType<JObject>(), call => (string?)call["tool"] == "eplan_connection_create"
            && (bool)call["succeeded"]! && ((JArray)call["objectOids"]!).Values<string>().SequenceEqual(["1"]));
        Assert.Contains(((JArray)report.ToJson()["calls"]!).OfType<JObject>(), call => (string?)call["tool"] == "eplan_connection_create"
            && !(bool)call["succeeded"]! && ((JArray)call["objectOids"]!).Values<string>().SequenceEqual(["2"]));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PropertyMoves_FreshTargetSlotIsUsedOnlyForAnUnambiguousDisplayedId(bool duplicateSource, bool duplicateTarget)
    {
        JObject owner = Device(1, "Function", "-K1", "K", 10);
        JObject property = new()
        {
            ["type"] = "ContextPropertyPlacement",
            ["oid"] = 2,
            ["parentOid"] = 1,
            ["propertyId"] = 20214,
            ["propertyIndex"] = 0,
            ["location"] = Point(4, 6),
            ["angle"] = Math.PI / 2,
        };
        JObject source = Page(owner, property);
        if (duplicateSource)
        {
            JObject duplicate = (JObject)property.DeepClone();
            duplicate["oid"] = 3;
            duplicate["propertyIndex"] = 1;
            ((JArray)source["placements"]!).Add(duplicate);
        }
        JObject targetOwner = Drawing(owner, 101);
        JObject targetProperty = new()
        {
            ["kind"] = "ContextPropertyPlacement",
            ["location"] = new JArray(0, 0),
            ["parent"] = targetOwner["locator"]!.DeepClone(),
            ["locator"] = new JObject { ["type"] = "ContextPropertyPlacement", ["oid"] = 102 },
            ["properties"] = new JObject { ["property_id"] = "20214", ["property_index"] = "7", ["angle_deg"] = "0" },
        };
        var drawings = new JArray(targetOwner, targetProperty);
        if (duplicateTarget)
        {
            JObject duplicate = (JObject)targetProperty.DeepClone();
            duplicate["locator"]!["oid"] = 103;
            duplicate["properties"]!["property_index"] = "8";
            drawings.Add(duplicate);
        }
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        Task<JObject> CallAsync(string tool, JObject arguments)
        {
            calls.Add((tool, (JObject)arguments.DeepClone()));
            JObject result = Success(arguments);
            if (tool == "eplan_page_get")
            { result["drawings"]!["items"] = drawings.DeepClone(); }
            return Task.FromResult(result);
        }
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        if (duplicateSource || duplicateTarget)
        {
            Assert.DoesNotContain(calls, call => call.Tool == "eplan_placement_update");
            Assert.Contains(((JArray)report.ToJson()["warnings"]!).OfType<JObject>(),
                warning => ((string?)warning["reason"])?.Contains("ambiguous", StringComparison.Ordinal) == true);
            return;
        }
        JObject update = Assert.Single(calls, call => call.Tool == "eplan_placement_update").Args;
        JToken move = Assert.Single((JArray)update["property_moves"]!);
        Assert.Equal((20214, 7, 14d, 26d, 90),
            ((int)move["property_id"]!, (int)move["property_index"]!, (double)move["x_mm"]!, (double)move["y_mm"]!, (int)move["angle_deg"]!));
        Assert.True(JToken.DeepEquals(targetOwner["locator"], move["parent"]));
        Assert.False((bool)update["dry_run"]!);
        Assert.False((bool)update["snap_to_grid"]!);
    }

    [Fact]
    public async Task Graphics_RejectedBatchRetainsEveryObjectAndContinuesWithTheNextBatch()
    {
        JObject source = Page([.. Enumerable.Range(1, 51).Select(index => Line(index, index))]);
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        int batches = 0;
        Task<JObject> CallAsync(string tool, JObject arguments)
        {
            calls.Add((tool, (JObject)arguments.DeepClone()));
            return Task.FromResult(tool == "eplan_placement_create" && ++batches == 1
                ? new JObject { ["error"] = new JObject { ["reason"] = "INVALID_ARGUMENT", ["message"] = "outside drawable area" } }
                : Success(arguments));
        }
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        JObject[] placements = [.. calls.Where(call => call.Tool == "eplan_placement_create").Select(call => call.Args)];
        Assert.Equal([50, 1], placements.Select(arguments => ((JArray)arguments["items"]!).Count));
        Assert.All(placements, arguments =>
        {
            Assert.False((bool)arguments["snap_to_grid"]!);
            Assert.False((bool)arguments["dry_run"]!);
        });
        Assert.Contains(calls, call => call.Tool == "eplan_connection_generate");
        JObject result = report.ToJson();
        Assert.Equal(1, (int)result["failedToolCallCount"]!);
        JToken failed = Assert.Single((JArray)result["calls"]!, call => !(bool)call["succeeded"]!);
        Assert.Equal(50, ((JArray)failed["objectOids"]!).Count);
        Assert.Contains("outside drawable area", (string)failed["error"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview_AbsentMacroFallsBackToTheExportedBoxAndGraphics()
    {
        JObject box = Device(1, "PLC", "-K1", "PLCC", 10);
        box["category"] = "PLCBox";
        box["parts"] = new JArray(new JObject { ["part"] = "FICTION.PLC", ["variant"] = "1", ["count"] = 1 });
        box["boundingBox"] = new JArray(Point(10, 20), Point(30, 50));
        JObject source = Page(box, Line(2, 40));
        source["pageType"] = "Overview";
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        Task<JObject> CallAsync(string tool, JObject arguments)
        {
            calls.Add((tool, (JObject)arguments.DeepClone()));
            return Task.FromResult(tool == "eplan_macro_insert"
                ? new JObject { ["error"] = new JObject { ["reason"] = "MACRO_NOT_FOUND", ["execution"] = "not_started" } }
                : Success(arguments));
        }
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        JObject macro = Assert.Single(calls, call => call.Tool == "eplan_macro_insert").Args;
        Assert.Equal(("FICTION.PLC", "1", "Overview", 0d, 292d),
            ((string?)macro["part"], (string?)macro["part_variant"], (string?)macro["representation"], (double)macro["x"]!, (double)macro["y"]!));
        Assert.False((bool)macro["dry_run"]!);
        JObject placed = Assert.Single(calls, call => call.Tool == "eplan_device_place").Args;
        Assert.Equal("PLCC", (string?)placed["symbol"]);
        Assert.Equal("-K1", (string?)placed["tag"]);
        Assert.Single(calls, call => call.Tool == "eplan_placement_create");
        Assert.Equal(1, (int)report.ToJson()["failedToolCallCount"]!);
        Assert.Contains(((JArray)report.ToJson()["notes"]!).OfType<JObject>(), note => ((string?)note["reason"])?.Contains("macro", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Devices_ThrownToolFailureStillAttemptsFollowingObjectsAndKeepsTheError()
    {
        JObject source = Page(Device(1, "Function", "-K1", "K", 10), Device(2, "Function", "-K2", "K", 30));
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        Task<JObject> CallAsync(string tool, JObject arguments)
        {
            calls.Add((tool, (JObject)arguments.DeepClone()));
            if (tool == "eplan_device_place" && (string?)arguments["tag"] == "-K1")
            { throw new IOException("fixture transport failed"); }
            return Task.FromResult(Success(arguments));
        }
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        Assert.Equal(["-K1", "-K2"], calls.Where(call => call.Tool == "eplan_device_place").Select(call => (string?)call.Args["tag"]));
        Assert.Contains(calls, call => call.Tool == "eplan_connection_generate");
        Assert.Equal(1, (int)report.ToJson()["failedToolCallCount"]!);
        Assert.Contains("fixture transport failed", (string)Assert.Single((JArray)report.ToJson()["failedToolCalls"]!)["reason"]!, StringComparison.Ordinal);
        Assert.All(calls.Where(call => call.Tool is "eplan_page_create" or "eplan_device_place" or "eplan_connection_generate"),
            call => Assert.False((bool)call.Args["dry_run"]!));
    }

    [Fact]
    public async Task Overview_SuccessfulMacroCoversOnlyConfirmedNewMembersOfItsSourceBlock()
    {
        JObject box = Device(1, "PLC", "-K1", "PLCC", 10);
        box["category"] = "PLCBox";
        box["parts"] = new JArray(new JObject { ["part"] = "FICTION.PLC", ["variant"] = "1" });
        box["boundingBox"] = new JArray(Point(10, 20), Point(30, 50));
        JObject member = Line(2, 20), unrelated = Line(3, 30);
        JObject block = new() { ["type"] = "Block", ["oid"] = 4, ["memberOids"] = new JArray(1, 2) };
        JObject source = Page(box, member, unrelated, block);
        source["pageType"] = "Overview";
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        int reads = 0;
        Task<JObject> CallAsync(string tool, JObject arguments)
        {
            calls.Add((tool, (JObject)arguments.DeepClone()));
            JObject response = Success(arguments);
            if (tool == "eplan_macro_insert")
            { response["devices"] = new JArray(new JObject { ["tag"] = "-K1", ["category"] = "PLCBox" }); }
            if (tool == "eplan_page_get" && ++reads > 1)
            { response["drawings"]!["items"] = new JArray(Drawing(box, 101), Drawing(member, 102), Drawing(unrelated, 103)); }
            return Task.FromResult(response);
        }
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        Assert.DoesNotContain(calls, call => call.Tool == "eplan_device_place");
        JObject graphics = Assert.Single(calls, call => call.Tool == "eplan_placement_create").Args;
        JToken graphic = Assert.Single((JArray)graphics["items"]!);
        Assert.Equal(30, (double)graphic["points"]![0]![0]!);
        Assert.Contains(((JArray)report.ToJson()["notes"]!).OfType<JObject>(),
            note => (string?)note["oid"] == "2" && ((string?)note["reason"])?.Contains("Covered", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Overview_RecoveredMacroUsesFreshDrawingsWithoutRenamingPlaceholderTags()
    {
        JObject box = Device(1, "PLC", "-K1", "PLCC", 10);
        box["category"] = "PLCBox";
        box["parts"] = new JArray(new JObject { ["part"] = "FICTION.PLC", ["variant"] = "1" });
        box["boundingBox"] = new JArray(Point(10, 20), Point(30, 50));
        JObject source = Page(box);
        source["pageType"] = "Overview";
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var calls = new List<(string Tool, JObject Args)>();
        int reads = 0;
        Task<JObject> CallAsync(string tool, JObject arguments)
        {
            calls.Add((tool, (JObject)arguments.DeepClone()));
            JObject response = Success(arguments);
            if (tool == "eplan_macro_insert")
            {
                response["recovered"] = true;
                response["devices"] = new JArray(new JObject { ["tag"] = "+-K?:?", ["category"] = "PLCBox" });
            }
            if (tool == "eplan_page_get" && ++reads > 1)
            { response["drawings"]!["items"] = new JArray(Drawing(box, 101)); }
            return Task.FromResult(response);
        }
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        Assert.DoesNotContain(calls, call => call.Tool == "eplan_device_update");
        Assert.DoesNotContain(calls, call => call.Tool == "eplan_device_place");
        Assert.Equal(2, reads);
        int macro = calls.FindIndex(call => call.Tool == "eplan_macro_insert");
        Assert.True(calls.FindIndex(call => call.Tool == "eplan_page_get") < macro);
        Assert.True(calls.FindLastIndex(call => call.Tool == "eplan_page_get") > macro);
    }

    [Fact]
    public void Report_RetainsNativeDifferencesAndAggregatesFailedCallsWithoutCountingObjectsAsCalls()
    {
        JObject source = Page(Line(1, 0), Line(2, 10), Line(3, 20));
        JObject target = Page(Line(101, 0), Line(102, 10), Line(103, 30));
        target["placements"]![1]!["pen"]!["color"] = 7;
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        report.AddPage("/1", source, target);
        var arguments = new JObject { ["items"] = new JArray(new JObject { ["kind"] = "line" }), ["dry_run"] = false };
        var failure = new JObject { ["reason"] = "INVALID_ARGUMENT", ["metadata"] = new JObject { ["rule"] = "overlap" } };
        report.RecordCall("/1", "eplan_placement_create", ["Line", "Line"], ["1", "2"], arguments, failure, false, "overlap");
        report.RecordCall("/2", "eplan_placement_create", ["Line"], ["3"], arguments, failure, false, "overlap");
        report.RecordCall("/1", "eplan_device_place", ["Function"], ["4"], new JObject(), failure, false, "outside");
        report.NoTool("/1", "PotentialDefinition", "5", "No tool (A86).");

        JObject result = report.ToJson();
        JToken counts = result["pages"]![0]!["types"]!["Line"]!;
        Assert.Equal((1, 1, 1, 1), ((int)counts["matched"]!, (int)counts["missing"]!, (int)counts["extra"]!, (int)counts["different"]!));
        Assert.Equal(0.25, (double)counts["matchingRate"]!);
        Assert.Equal("pen.color", (string?)Assert.Single((JArray)counts["differentObjects"]![0]!["fields"]!)["field"]);
        Assert.Equal("pen.color", (string?)Assert.Single((JArray)result["differentFields"]!)["field"]);
        Assert.Equal(3, (int)result["failedToolCallCount"]!);
        JToken mostFrequent = result["failedToolCalls"]![0]!;
        Assert.Equal(("eplan_placement_create", "overlap", "Line", 2),
            ((string?)mostFrequent["tool"], (string?)mostFrequent["reason"], (string?)mostFrequent["objectType"], (int)mostFrequent["callCount"]!));
        Assert.Equal([1, 2], ((JArray)mostFrequent["callSequences"]!).Values<int>());
        Assert.True(JToken.DeepEquals(arguments, result["calls"]![0]!["arguments"]));
        Assert.True(JToken.DeepEquals(failure, result["calls"]![0]!["result"]));
        Assert.Equal("PotentialDefinition", (string?)Assert.Single((JArray)result["noToolTypes"]!)["objectType"]);
        Assert.Contains("overlap", report.ToMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public void Report_UnreadableTargetIsIncompleteAndPreservesEveryMissingObject()
    {
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        report.AddPage("/1", Page(Line(1, 0), Line(2, 10)), null);

        JObject result = report.ToJson();
        JToken page = Assert.Single((JArray)result["pages"]!);
        Assert.False((bool)page["complete"]!);
        Assert.False((bool)page["targetAvailable"]!);
        Assert.Equal(2, (int)page["types"]!["Line"]!["missing"]!);
        Assert.Equal(0, (double)page["totals"]!["matchingRate"]!);
        Assert.Single((JArray)result["warnings"]!);
        Assert.Empty((JArray)result["infrastructureErrors"]!);
    }

    [Fact]
    public void Report_ReportPageUsesOriginalFieldComparisonWithoutClaimingAnObjectMatchRate()
    {
        JObject source = Page(Line(1, 0));
        source["isReportPage"] = true;
        source["report"] = new JArray(new JObject { ["type"] = "PartsList", ["form"] = "source-form" });
        JObject target = (JObject)source.DeepClone();
        target["report"]![0]!["form"] = "different-form";
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        report.AddPage("/1", source, target);

        JToken page = Assert.Single((JArray)report.ToJson()["pages"]!);
        Assert.Empty((JObject)page["types"]!);
        Assert.Equal(JTokenType.Null, page["totals"]!["matchingRate"]!.Type);
        Assert.Equal("report[0].form", (string?)Assert.Single((JArray)page["pageDifferences"]!)["field"]);
    }

    private static JObject Page(params JObject[] placements) => new()
    {
        ["name"] = "/1",
        ["pageType"] = "Circuit",
        ["placements"] = new JArray(placements),
        ["connections"] = new JArray(),
    };

    private static JObject Line(int oid, int x) => new()
    {
        ["type"] = "Line",
        ["oid"] = oid,
        ["location"] = Point(x, 20),
        ["start"] = Point(x, 20),
        ["end"] = Point(x + 1, 20),
        ["pen"] = new JObject { ["color"] = 1 },
    };

    private static JObject Device(int oid, string type, string name, string symbol, int x) => new()
    {
        ["type"] = type,
        ["oid"] = oid,
        ["name"] = name,
        ["location"] = Point(x, 20),
        ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = symbol, ["variant"] = 0 },
        ["pins"] = new JArray(),
        ["parts"] = new JArray(),
    };

    private static JObject Success(JObject arguments) => new()
    {
        ["execution"] = "committed",
        ["device"] = new JObject { ["tag"] = arguments["tag"]?.DeepClone() },
        ["drawings"] = new JObject { ["items"] = new JArray(), ["nextCursor"] = null, ["truncated"] = false },
    };

    private static JObject Drawing(JObject source, int oid) => new()
    {
        ["kind"] = source["type"]!.DeepClone(),
        ["location"] = new JArray(source["location"]!["x"]!.DeepClone(), source["location"]!["y"]!.DeepClone()),
        ["locator"] = new JObject { ["type"] = source["type"]!.DeepClone(), ["oid"] = oid },
        ["properties"] = new JObject { ["name"] = source["name"]?.DeepClone() },
        ["symbol"] = source["symbol"]?.DeepClone(),
    };

    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
}
