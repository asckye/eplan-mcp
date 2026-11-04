using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawRoundThreeTests
{
    [Fact]
    public async Task Pages_PreserveEveryExportedDescriptionLanguage()
    {
        JObject source = Page();
        string[] languages = ["de_DE", "en_US", "zh_CN", "cs_CZ", "da_DK", "es_ES", "fi_FI", "fr_FR", "hu_HU", "it_IT",
            "ja_JP", "ko_KR", "nl_NL", "no_NO", "pl_PL", "pt_PT", "ro_RO", "ru_RU", "sv_SE", "tr_TR"];
        source["description"] = new JObject(languages.Select(language => new JProperty(language, "Synthetic " + language)));
        var rig = new Rig();

        await rig.Driver.DrawPageAsync(source, [source]);

        JObject call = Assert.Single(rig.Calls, call => call.Tool == "eplan_page_create").Args;
        Assert.True(JToken.DeepEquals(source["description"], call["pages"]![0]!["description"]));
    }

    [Fact]
    public async Task Potentials_PreserveNonemptyNamesAndFollowDevicesAndConnections()
    {
        JObject node = Device(2, "InterruptionPoint", "-UNPAIRED", 40);
        node["direction"] = "Left";
        JObject definition = Device(3, "ConnectionDefinitionPoint", "", 60);
        definition["parts"] = new JArray(new JObject { ["part"] = "SYNTHETIC.PART" });
        JObject potential = Potential(4, "+24V", "", 80);
        JObject signal = Potential(5, "", "SUPPLY", 100);
        JObject unreadable = Potential(6, "", "", 120);
        JObject source = Page(Device(1, "Function", "-K1", 20), node, definition, potential, signal, unreadable);
        var rig = new Rig();

        await rig.Driver.DrawPageAsync(source, [source]);

        int potentialCall = rig.Calls.FindIndex(call => call.Tool == "eplan_connection_create"
            && (string?)call.Args["items"]![0]!["kind"] == "potential_definition");
        Assert.True(potentialCall > rig.Calls.FindIndex(call => call.Tool == "eplan_device_place"));
        Assert.True(potentialCall > rig.Calls.FindIndex(call => call.Tool == "eplan_connection_generate"));
        Assert.True(potentialCall > rig.Calls.FindIndex(call => call.Tool == "eplan_connection_create"
            && (string?)call.Args["items"]![0]!["kind"] == "definition_point"));
        JObject request = rig.Calls[potentialCall].Args;
        Assert.Equal("warn", (string?)request["layout_guard"]);
        var points = ((JArray)request["items"]!).OfType<JObject>().ToArray();
        Assert.Equal(2, points.Length);
        Assert.Equal("+24V", (string?)points[0]["potential_name"]);
        Assert.Null(points[0]["signal_name"]);
        Assert.Equal("SUPPLY", (string?)points[1]["signal_name"]);
        Assert.Null(points[1]["potential_name"]);
        Assert.Contains(((JArray)rig.Report.ToJson()["warnings"]!).OfType<JObject>(),
            warning => ((string?)warning["reason"])?.Contains("Potential definition 6", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Definitions_PlacePartOnlyPointWithVariantAndCount()
    {
        JObject point = Device(1, "ConnectionDefinitionPoint", "", 40);
        point["parts"] = new JArray(new JObject { ["part"] = "SYNTHETIC.PART", ["variant"] = "2", ["count"] = 3 });
        JObject source = Page(point);
        var rig = new Rig();

        await rig.Driver.DrawPageAsync(source, [source]);

        JObject request = Assert.Single(rig.Calls, call => call.Tool == "eplan_connection_create").Args;
        JToken item = Assert.Single((JArray)request["items"]!);
        Assert.Equal("definition_point", (string?)item["kind"]);
        Assert.Equal("SYNTHETIC.PART", (string?)item["part"]);
        Assert.Equal("2", (string?)item["part_variant"]);
        Assert.Equal(3, (int)item["part_count"]!);
        Assert.Null(item["color"]);
        Assert.Null(item["cross_section"]);
        Assert.Null(item["designation"]);
    }

    [Fact]
    public async Task PropertyStyles_SendOnlyChangedFieldsAndUseAbsolutePositionWithLayerAngle()
    {
        JObject owner = Device(1, "Function", "-K1", 100);
        owner["location"] = Point(100, 200);
        JObject property = Property(2, 1, -4, -8, -16002);
        property["height"] = 3.5;
        property["alignment"] = "BaseLineLeft";
        property["isVisible"] = false;
        property["layer"] = 8;
        property["pen"] = new JObject { ["color"] = 4, ["width"] = 0.25, ["style"] = 1, ["patternLength"] = 2, ["lineEndType"] = 0 };
        JObject targetOwner = Drawing(owner, 101);
        JObject targetProperty = PropertyDrawing(targetOwner, 102, 96, 192, "from_layer");
        targetProperty["properties"]!["height_raw_mm"] = "3";
        targetProperty["properties"]!["alignment"] = "BaseLineLeft";
        targetProperty["properties"]!["visible"] = "False";
        targetProperty["properties"]!["layer"] = "8";
        targetProperty["properties"]!["pen_color"] = "5";
        targetProperty["properties"]!["pen_width_mm"] = "0.25";
        targetProperty["properties"]!["pen_style"] = "1";
        targetProperty["properties"]!["pen_pattern_length"] = "2";
        targetProperty["properties"]!["pen_line_end"] = "0";
        JObject source = Page(owner, property);
        var rig = new Rig((tool, args) => tool == "eplan_page_get"
            ? PageRead(new JArray(targetOwner.DeepClone(), targetProperty.DeepClone())) : Success(args));

        await rig.Driver.DrawPageAsync(source, [source]);

        JObject request = Assert.Single(rig.Calls, call => call.Tool == "eplan_placement_update").Args;
        var move = Assert.IsType<JObject>(Assert.Single((JArray)request["property_moves"]!));
        Assert.Equal((96d, 192d, -16002), ((double)move["x_mm"]!, (double)move["y_mm"]!, (int)move["angle_deg"]!));
        Assert.Equal(3.5, (double)move["height_mm"]!);
        Assert.True(JToken.DeepEquals(new JObject { ["color"] = 4 }, move["pen"]));
        Assert.Null(move["alignment"]);
        Assert.Null(move["visible"]);
        Assert.Null(move["layer"]);
        Assert.True(JToken.DeepEquals(targetOwner["locator"], move["parent"]));
    }

    [Fact]
    public async Task PropertyStyles_EquivalentReadbackNeedsNoWrite()
    {
        JObject owner = Device(1, "Function", "-K1", 10);
        JObject property = Property(2, 1, 4, 6, Math.PI / 2);
        JObject targetOwner = Drawing(owner, 101);
        JObject targetProperty = PropertyDrawing(targetOwner, 102, 14, 26, "90");
        JObject source = Page(owner, property);
        var rig = new Rig((tool, args) => tool == "eplan_page_get"
            ? PageRead(new JArray(targetOwner.DeepClone(), targetProperty.DeepClone())) : Success(args));

        await rig.Driver.DrawPageAsync(source, [source]);

        Assert.DoesNotContain(rig.Calls, call => call.Tool == "eplan_placement_update");
    }

    [Fact]
    public async Task PropertyPhase_SharesOnePaginatedSnapshotAcrossAllOwners()
    {
        JObject first = Device(1, "Function", "-K1", 10);
        JObject second = Device(2, "Function", "-K2", 30);
        JObject source = Page(first, second, Property(3, 1, 4, 6, 0), Property(4, 2, 4, 6, 0));
        JObject firstTarget = Drawing(first, 101), secondTarget = Drawing(second, 102);
        var rig = new Rig((tool, args) => tool == "eplan_page_get"
            ? args["drawing_cursor"] is null
                ? PageRead(new JArray(firstTarget.DeepClone(), PropertyDrawing(firstTarget, 103, 0, 0, "0")), "next")
                : PageRead(new JArray(secondTarget.DeepClone(), PropertyDrawing(secondTarget, 104, 0, 0, "0")))
            : Success(args));

        await rig.Driver.DrawPageAsync(source, [source]);

        Assert.Equal(2, rig.Calls.Count(call => call.Tool == "eplan_page_get"));
        JObject update = Assert.Single(rig.Calls, call => call.Tool == "eplan_placement_update").Args;
        Assert.Equal(2, ((JArray)update["property_moves"]!).Count);
        Assert.Equal("next", (string?)rig.Calls.Last(call => call.Tool == "eplan_page_get").Args["drawing_cursor"]);
    }

    [Fact]
    public async Task DrawingCache_WritesInvalidateButUnchangedPhasesReuseTheSnapshot()
    {
        JObject owner = new() { ["type"] = "Block", ["oid"] = 1, ["location"] = Point(10, 20) };
        JObject source = Page(owner, Property(2, 1, 4, 6, 0));
        JObject targetOwner = Drawing(owner, 101);
        bool moved = false;
        var rig = new Rig((tool, args) =>
        {
            if (tool == "eplan_connection_generate")
            { return new JObject { ["execution"] = "not_started" }; }
            if (tool == "eplan_placement_update")
            { moved = true; }
            return tool == "eplan_page_get"
                ? PageRead(new JArray(targetOwner.DeepClone(), PropertyDrawing(targetOwner, 102, moved ? 14 : 0, moved ? 26 : 0, "0")))
                : Success(args);
        });

        await rig.Driver.DrawPageAsync(source, [source]);
        await rig.Driver.DrawPageAsync(source, [source]);
        await rig.Driver.DrawPageAsync(source, [source]);

        Assert.Single(rig.Calls, call => call.Tool == "eplan_placement_update");
        Assert.Equal(2, rig.Calls.Count(call => call.Tool == "eplan_page_get"));
    }

    [Fact]
    public async Task Export_OpensCoverBeforeReadingHiddenText()
    {
        var rig = new Rig();

        await rig.Driver.PrepareExportAsync();

        var call = Assert.Single(rig.Calls);
        Assert.Equal("eplan_page_open", call.Tool);
        Assert.Equal("&AAA1/1", (string?)call.Args["page"]);
    }

    [Fact]
    public async Task Report_RecordsPageDurationAndDrawingReadCount()
    {
        JObject owner = Device(1, "Function", "-K1", 10);
        JObject source = Page(owner, Property(2, 1, 4, 6, 0));
        JObject targetOwner = Drawing(owner, 101);
        var rig = new Rig((tool, args) => tool == "eplan_page_get"
            ? PageRead(new JArray(targetOwner.DeepClone(), PropertyDrawing(targetOwner, 102, 14, 26, "0"))) : Success(args));

        await rig.Driver.DrawPageAsync(source, [source]);

        JToken timing = Assert.Single((JArray)rig.Report.ToJson()["pageTimings"]!);
        Assert.Equal("/1", (string?)timing["page"]);
        Assert.True((double)timing["elapsedMilliseconds"]! >= 0);
        Assert.Equal(1, (int)timing["drawingReadCalls"]!);
    }

    [Fact]
    public void Report_ListsEverySubsetDifferenceWithoutChangingNativeMatchRate()
    {
        JObject circuit = Page(new JObject
        {
            ["type"] = "Line",
            ["oid"] = 1,
            ["location"] = Point(10, 20),
            ["start"] = Point(10, 20),
            ["end"] = Point(30, 20),
        });
        circuit["properties"] = new JObject { ["11040"] = "NEXT-SOURCE" };
        JObject targetCircuit = (JObject)circuit.DeepClone();
        targetCircuit["placements"]![0]!["oid"] = 101;
        targetCircuit["properties"]!["11040"] = "NEXT-TARGET";
        JObject reportPage = new()
        {
            ["name"] = "/2",
            ["pageType"] = "Report",
            ["isReportPage"] = true,
            ["properties"] = new JObject { ["11011"] = "Generated source description" },
            ["report"] = new JArray(new JObject { ["deviceTag"] = "SOURCE-DEVICE-SET", ["isAutomaticPageDescription"] = true }),
        };
        JObject targetReport = (JObject)reportPage.DeepClone();
        ((JObject)targetReport["properties"]!).Remove("11011");
        targetReport["report"]![0]!["deviceTag"] = "TARGET-DEVICE-SET";
        var report = new SmallProjectRedrawReport("synthetic", "ScratchTest");

        report.AddPage("/1", circuit, targetCircuit);
        report.AddPage("/2", reportPage, targetReport);

        JObject json = report.ToJson();
        var pages = ((JArray)json["pages"]!).OfType<JObject>().ToArray();
        Assert.Equal(3, (int)json["subsetDifferenceCount"]!);
        Assert.Equal(1, (int)pages[0]["subsetDifferenceCount"]!);
        Assert.Equal(2, (int)pages[1]["subsetDifferenceCount"]!);
        Assert.All(pages, page => Assert.Empty((JArray)page["pageDifferences"]!));
        Assert.Equal(1, (int)pages[0]["types"]!["Line"]!["matched"]!);
        Assert.Equal(1d, (double)json["totals"]!["matchingRate"]!);
        Assert.Empty((JArray)json["differentFields"]!);
        var differences = ((JArray)json["subsetDifferences"]!).OfType<JObject>()
            .ToDictionary(item => (string)item["difference"]!["field"]!, item => item["difference"]!);
        Assert.Equal(3, differences.Count);
        Assert.Equal("NEXT-SOURCE", (string?)differences["properties.11040"]["source"]);
        Assert.Equal("NEXT-TARGET", (string?)differences["properties.11040"]["target"]);
        Assert.Equal("SOURCE-DEVICE-SET", (string?)differences["report[0].deviceTag"]["source"]);
        Assert.Equal("TARGET-DEVICE-SET", (string?)differences["report[0].deviceTag"]["target"]);
        Assert.Equal("Generated source description", (string?)differences["properties.11011"]["source"]);
        Assert.Equal(JTokenType.Null, differences["properties.11011"]["target"]!.Type);
        string markdown = report.ToMarkdown();
        foreach (string expected in new[] { "Next page", "Report device tags", "Page description (automatic report only)",
            "NEXT-SOURCE", "NEXT-TARGET", "SOURCE-DEVICE-SET", "TARGET-DEVICE-SET", "Generated source description", "null" })
        { Assert.Contains(expected, markdown, StringComparison.Ordinal); }
    }

    private sealed class Rig
    {
        internal List<(string Tool, JObject Args)> Calls { get; } = [];
        internal SmallProjectRedrawReport Report { get; } = new("synthetic", "ScratchTest");
        internal SmallProjectRedrawDriver Driver { get; }

        internal Rig(Func<string, JObject, JObject>? respond = null)
        {
            Driver = new SmallProjectRedrawDriver((tool, args) =>
            {
                Calls.Add((tool, (JObject)args.DeepClone()));
                return Task.FromResult(respond?.Invoke(tool, args) ?? Success(args));
            }, "ScratchTest", Path.GetTempPath(), Report);
        }
    }

    private static JObject Page(params JObject[] placements) => new()
    {
        ["name"] = "/1",
        ["pageType"] = "Circuit",
        ["placements"] = new JArray(placements),
        ["connections"] = new JArray(),
    };

    private static JObject Device(int oid, string type, string name, double x) => new()
    {
        ["type"] = type,
        ["oid"] = oid,
        ["name"] = name,
        ["location"] = Point(x, 20),
        ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = "K", ["variant"] = 0 },
        ["pins"] = new JArray(),
        ["parts"] = new JArray(),
    };

    private static JObject Potential(int oid, string potential, string signal, double x) => new()
    {
        ["type"] = "PotentialDefinition",
        ["oid"] = oid,
        ["location"] = Point(x, 20),
        ["potentialName"] = potential,
        ["signalName"] = signal,
    };

    private static JObject Property(int oid, int ownerOid, double x, double y, double angle) => new()
    {
        ["type"] = "ContextPropertyPlacement",
        ["oid"] = oid,
        ["parentOid"] = ownerOid,
        ["propertyId"] = 20214,
        ["propertyIndex"] = 0,
        ["location"] = Point(x, y),
        ["angle"] = angle,
    };

    private static JObject Drawing(JObject source, int oid) => new()
    {
        ["kind"] = source["type"]!.DeepClone(),
        ["locator"] = new JObject { ["type"] = source["type"]!.DeepClone(), ["oid"] = oid },
        ["location"] = new JArray(source["location"]!["x"]!.DeepClone(), source["location"]!["y"]!.DeepClone()),
        ["properties"] = new JObject { ["name"] = source["name"]?.DeepClone() },
        ["symbol"] = source["symbol"]?.DeepClone(),
    };

    private static JObject PropertyDrawing(JObject owner, int oid, double x, double y, string angle) => new()
    {
        ["kind"] = "ContextPropertyPlacement",
        ["locator"] = new JObject { ["type"] = "ContextPropertyPlacement", ["oid"] = oid },
        ["location"] = new JArray(x, y),
        ["parent"] = owner["locator"]!.DeepClone(),
        ["properties"] = new JObject { ["property_id"] = "20214", ["property_index"] = "7", ["angle_deg"] = angle },
    };

    private static JObject PageRead(JArray items, string? cursor = null) => new()
    {
        ["drawings"] = new JObject { ["items"] = items, ["nextCursor"] = cursor, ["truncated"] = cursor is not null },
    };

    private static JObject Success(JObject args) => new()
    {
        ["execution"] = "committed",
        ["device"] = new JObject { ["tag"] = args["tag"]?.DeepClone() },
        ["drawings"] = new JObject { ["items"] = new JArray(), ["nextCursor"] = null, ["truncated"] = false },
    };

    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
}
