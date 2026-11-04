using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawPartsTests
{
    private const string PageName = "=GAA+A1&EFS1/1";
    private const string Tag = "=GAA+A1-FC1";
    private const string NativeSymbol = "IEC_symbol;1;1547;6";

    [Fact]
    public async Task MainFunction_UsesPartAndPreservesItsCoveredGraphics()
    {
        var rig = new Rig { IncludeLine = true };

        await rig.Draw(includeLine: true);

        JObject placement = Assert.Single(rig.Arguments("eplan_device_place"));
        Assert.Equal("SIE.3VA1032-2ED32-0AA0", (string?)placement["part"]);
        Assert.Equal("1", (string?)placement["variant"]);
        Assert.Equal(Tag, (string?)placement["tag"]);
        Assert.Equal(96, (double)placement["x"]!);
        Assert.Equal(260, (double)placement["y"]!);
        Assert.Null(placement["library"]);
        Assert.Equal("SIE.3VA9157-0FK25", (string?)Assert.Single(rig.Arguments("eplan_device_update"))["part"]);
        Assert.Empty(rig.Arguments("eplan_placement_create"));
        JObject query = Assert.Single(rig.Arguments("eplan_property_get"));
        JToken locator = query["queries"]![0]!["target"]!["locator"]!;
        Assert.Equal(17, (int)locator["type"]!);
        Assert.Equal("ScratchTest", (string?)locator["project"]);
        Assert.Equal("function", (string?)locator["kind"]);
        Assert.Equal(20575, (int)query["queries"]![0]!["properties"]![0]!["property_id"]!);
        JObject statistics = (JObject)rig.Report.ToJson()["originStatistics"]!;
        Assert.Equal(2, (int)statistics["part"]!["count"]!);
        Assert.Equal(1d, (double)statistics["part"]!["share"]!);
        Assert.Equal(0, (int)statistics["manual_graphics"]!["count"]!);
    }

    [Theory]
    [InlineData("not_started")]
    [InlineData("rolled_back")]
    public async Task RejectedPart_UsesSymbolFallbackAndAssignsAllSourceParts(string execution)
    {
        var rig = new Rig { PartFailure = execution };

        await rig.Draw();

        JObject[] placements = rig.Arguments("eplan_device_place");
        Assert.Equal(2, placements.Length);
        Assert.NotNull(placements[0]["part"]);
        Assert.Equal("IEC_symbol", (string?)placements[1]["library"]);
        Assert.Equal("Q_3LI", (string?)placements[1]["symbol"]);
        Assert.Equal(6, (int)placements[1]["symbol_variant"]!);
        Assert.Null(placements[1]["part"]);
        Assert.Equal(["SIE.3VA1032-2ED32-0AA0", "SIE.3VA9157-0FK25"],
            rig.Arguments("eplan_device_update").Select(call => (string)call["part"]!).ToArray());
        Assert.Equal("symbol_fallback", (string?)Assert.Single((JArray)rig.Report.ToJson()["objectOrigins"]!)["origin"]);
        Assert.Equal(1, (int)rig.Report.ToJson()["failedToolCallCount"]!);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData(null)]
    public async Task UnknownPartOutcome_DoesNotReplaySymbolFallback(string? execution)
    {
        var rig = new Rig { PartFailure = execution ?? "absent" };

        await rig.Draw();

        Assert.Single(rig.Arguments("eplan_device_place"));
        Assert.Empty(rig.Arguments("eplan_device_update"));
        Assert.Empty((JArray)rig.Report.ToJson()["objectOrigins"]!);
        Assert.Contains(((JArray)rig.Report.ToJson()["warnings"]!).OfType<JObject>(),
            warning => ((string?)warning["reason"])?.Contains("outcome is unknown", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task CommittedDifferentNativeSymbol_FallsBackAndRetainsOriginalCommitEvidence()
    {
        var rig = new Rig { ReadNativeSymbol = "IEC_symbol;1;1548;6" };

        await rig.Draw();

        JObject[] placements = rig.Arguments("eplan_device_place");
        Assert.Equal(2, placements.Length);
        Assert.True((bool)placements[1]["join_device"]!);
        Assert.Equal("Q_3LI", (string?)placements[1]["symbol"]);
        Assert.Empty(rig.Arguments("eplan_placement_delete"));
        // The first part already belongs to the committed device; only its additional part is appended.
        Assert.Equal("SIE.3VA9157-0FK25", (string?)Assert.Single(rig.Arguments("eplan_device_update"))["part"]);
        JObject report = rig.Report.ToJson();
        Assert.Equal("symbol_fallback", (string?)Assert.Single((JArray)report["objectOrigins"]!)["origin"]);
        Assert.Contains(((JArray)report["notes"]!).OfType<JObject>(),
            note => ((string?)note["reason"])?.Contains("cannot be removed", StringComparison.Ordinal) == true);
        Assert.Contains(((JArray)report["calls"]!).OfType<JObject>(), call => (string?)call["tool"] == "eplan_property_get"
            && (string?)call["result"]?["items"]?[0]?["value"] == "IEC_symbol;1;1548;6");
    }

    [Fact]
    public async Task IncompleteBeforeRead_DoesNotClaimOldGraphicsAsPartCoverage()
    {
        var rig = new Rig { IncompleteBefore = true, IncludeLine = true };

        await rig.Draw(includeLine: true);

        Assert.Single(rig.Arguments("eplan_device_place"));
        JObject graphicCall = Assert.Single(rig.Arguments("eplan_placement_create"));
        Assert.Equal("line", (string?)graphicCall["items"]![0]!["kind"]);
        Assert.Contains(((JArray)rig.Report.ToJson()["warnings"]!).OfType<JObject>(),
            warning => ((string?)warning["reason"])?.Contains("incomplete pre-insertion read", StringComparison.Ordinal) == true);
        Assert.Equal(1, (int)rig.Report.ToJson()["originStatistics"]!["manual_graphics"]!["count"]!);
    }

    [Fact]
    public void Report_ListsFinalOriginsAndMacroVersionObjectsWithoutSuppressingComparison()
    {
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        JObject[] sources = [Device(), Line(), Object(3, "Rectangle"), Object(4, "Text")];
        string[] origins = ["macro", "part", "symbol_fallback", "manual_graphics"];
        for (int index = 0; index < sources.Length; index++)
        { report.RecordOrigin(PageName, sources[index], origins[index]); }
        report.RecordPageElapsed(PageName, TimeSpan.Zero);
        report.RecordMacroVersionDifference(PageName, sources[1], "PXC/2703981.ema", "PXC/2703994.ema", "The replacement macro differs.");
        report.AddPage(PageName, Page(sources), Page([]));

        JObject json = report.ToJson();
        Assert.Equal(4, (int)json["originStatistics"]!["total"]!);
        foreach (string origin in origins)
        {
            Assert.Equal(1, (int)json["originStatistics"]![origin]!["count"]!);
            Assert.Equal(0.25, (double)json["originStatistics"]![origin]!["share"]!);
        }
        Assert.Equal(1, (int)json["originStatistics"]!["manual_graphics"]!["types"]!["Text"]!);
        Assert.Single((JArray)json["pageOriginStatistics"]!);
        JToken difference = Assert.Single((JArray)json["macroVersionDifferences"]!);
        Assert.Equal("PXC/2703981.ema", (string?)difference["requestedMacro"]);
        Assert.True(JToken.DeepEquals(sources[1], difference["source"]));
        Assert.Equal(4, (int)json["totals"]!["missing"]!);
        Assert.Contains("Macro version differences", report.ToMarkdown(), StringComparison.Ordinal);
    }

    private sealed class Rig
    {
        internal string? PartFailure { get; init; }
        internal string ReadNativeSymbol { get; init; } = NativeSymbol;
        internal bool IncludeLine { get; init; }
        internal bool IncompleteBefore { get; init; }
        internal SmallProjectRedrawReport Report { get; } = new("test", "ScratchTest");
        private readonly List<(string Tool, JObject Args)> _calls = [];
        private bool _partCommitted;

        internal async Task Draw(bool includeLine = false)
        {
            JObject page = Page(includeLine ? [Device(), Line()] : [Device()]);
            var driver = new SmallProjectRedrawDriver((tool, args) =>
            {
                _calls.Add((tool, (JObject)args.DeepClone()));
                if (tool == "eplan_device_place" && args["part"] is not null)
                {
                    if (PartFailure is not null)
                    {
                        var error = new JObject { ["reason"] = "PART_NOT_FOUND" };
                        if (PartFailure != "absent")
                        { error["execution"] = PartFailure; }
                        return Task.FromResult(new JObject { ["error"] = error });
                    }
                    _partCommitted = true;
                }
                if (tool == "eplan_page_get")
                {
                    var drawings = new JArray();
                    if (_partCommitted)
                    {
                        drawings.Add(Drawing("Function", 17, 182, 96, 260, new JObject { ["name"] = Tag }));
                        if (IncludeLine)
                        {
                            JObject line = Drawing("Line", 3, 183, 192, 68, new JObject { ["layer"] = "306" });
                            line["bounds"] = new JArray(192, 68, 280, 68);
                            drawings.Add(line);
                        }
                    }
                    return Task.FromResult(new JObject
                    {
                        ["drawings"] = new JObject
                        { ["items"] = drawings, ["truncated"] = !_partCommitted && IncompleteBefore }
                    });
                }
                if (tool == "eplan_property_get")
                {
                    return Task.FromResult(new JObject
                    {
                        ["items"] = new JArray(new JObject
                        { ["property_id"] = 20575, ["index"] = 0, ["status"] = "value", ["value"] = ReadNativeSymbol })
                    });
                }
                return Task.FromResult(new JObject { ["execution"] = "committed", ["device"] = new JObject { ["tag"] = Tag } });
            }, "ScratchTest", Path.GetTempPath(), Report);
            await driver.DrawPageAsync(page, [page]);
        }

        internal JObject[] Arguments(string tool) => _calls.Where(call => call.Tool == tool).Select(call => call.Args).ToArray();
    }

    private static JObject Device() => new()
    {
        ["type"] = "Function",
        ["oid"] = 27224,
        ["name"] = Tag,
        ["isMain"] = true,
        ["location"] = Point(96, 260),
        ["symbol"] = new JObject { ["library"] = "IEC_symbol", ["name"] = "Q_3LI", ["variant"] = 6 },
        ["parts"] = new JArray(new JObject { ["part"] = "SIE.3VA1032-2ED32-0AA0", ["variant"] = "1", ["count"] = 1 },
            new JObject { ["part"] = "SIE.3VA9157-0FK25", ["variant"] = "1", ["count"] = 1 }),
        ["properties"] = new JArray(new JObject { ["id"] = 20575, ["index"] = 0, ["value"] = NativeSymbol }),
    };

    private static JObject Line() => new()
    {
        ["type"] = "Line",
        ["oid"] = 2,
        ["location"] = Point(192, 68),
        ["start"] = Point(192, 68),
        ["end"] = Point(280, 68),
        ["layer"] = 306,
        ["boundingBox"] = new JArray(Point(192, 68), Point(280, 68)),
    };

    private static JObject Drawing(string kind, int type, int oid, double x, double y, JObject properties) => new()
    {
        ["kind"] = kind,
        ["locator"] = new JObject { ["project"] = "ScratchTest", ["page"] = PageName, ["type"] = type, ["oid"] = oid },
        ["location"] = new JArray(x, y),
        ["parent"] = null,
        ["properties"] = properties,
    };

    private static JObject Object(int oid, string type) => new() { ["type"] = type, ["oid"] = oid, ["location"] = Point(oid, 20) };
    private static JObject Page(JObject[] objects) => new() { ["name"] = PageName, ["pageType"] = "Circuit", ["placements"] = new JArray(objects), ["connections"] = new JArray() };
    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
}
