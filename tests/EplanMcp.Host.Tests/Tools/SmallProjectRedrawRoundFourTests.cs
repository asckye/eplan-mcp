using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawRoundFourTests
{
    [Fact]
    public async Task Nodes_BatchBeforeOneGenerationAndDefinitionsFollowIt()
    {
        JObject corner = Node(1, 252, 108, "Left", "Up");
        JObject tee = Node(2, 96, 248, "Up", "Down", "Right");
        tee["symbol"] = new JObject { ["variant"] = 1 };
        JObject interruption = new()
        {
            ["type"] = "InterruptionPoint",
            ["oid"] = 3,
            ["name"] = "-2L1",
            ["direction"] = "Left",
            ["location"] = Point(376, 284),
            ["symbol"] = new JObject { ["name"] = "BP" },
            ["properties"] = new JArray(new JObject { ["id"] = 24809, ["value"] = "100" }),
        };
        JObject definition = new()
        {
            ["type"] = "ConnectionDefinitionPoint",
            ["oid"] = 4,
            ["location"] = Point(252, 144),
            ["symbol"] = new JObject { ["name"] = "CDP", ["variant"] = 0 },
        };
        JObject potential = new()
        {
            ["type"] = "PotentialDefinition",
            ["oid"] = 5,
            ["location"] = Point(144, 72),
            ["potentialName"] = "N",
        };
        JObject device = new()
        {
            ["type"] = "Function",
            ["oid"] = 6,
            ["name"] = "-K1",
            ["location"] = Point(20, 20),
            ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = "K", ["variant"] = 0 },
        };
        JObject source = Page(corner, tee, interruption, definition, potential, device);
        var rig = new Rig();

        await rig.Driver.DrawPageAsync(source, [source]);

        var connectionCalls = rig.Calls.Where(call => call.Tool is "eplan_connection_create" or "eplan_connection_generate").ToArray();
        Assert.Equal(["eplan_connection_create", "eplan_connection_generate", "eplan_connection_create", "eplan_connection_create"],
            connectionCalls.Select(call => call.Tool));
        JObject nodes = connectionCalls[0].Args;
        Assert.False((bool)nodes["generate"]!);
        Assert.Equal(["corner", "t_node", "interruption_point"], ((JArray)nodes["items"]!).Select(item => (string?)item["kind"]));
        Assert.True(JToken.DeepEquals(new JArray(252d, 108d), nodes["items"]![0]!["position"]));
        Assert.Equal(1, (int)nodes["items"]![1]!["variant"]!);
        Assert.Equal(100, (int)nodes["items"]![2]!["sort_code"]!);
        Assert.Equal("definition_point", (string?)connectionCalls[2].Args["items"]![0]!["kind"]);
        Assert.False((bool)connectionCalls[3].Args["generate"]!);
        Assert.True(rig.Calls.FindIndex(call => call.Tool == "eplan_device_place")
            < rig.Calls.FindIndex(call => call.Tool == "eplan_connection_create"));
        Assert.Equal(0, (int)rig.Report.ToJson()["failedToolCallCount"]!);
    }

    [Fact]
    public async Task Nodes_UseFiftyItemBatchesAndRetainUnmappableObjectEvidence()
    {
        JObject[] nodes = Enumerable.Range(1, 101).Select(id => Node(id, id, 108, "Left", "Up")).ToArray();
        JObject invalid = Node(102, 0, 0, "Up", "Down", "Left", "Right");
        JObject source = Page([.. nodes, invalid]);
        var rig = new Rig();

        await rig.Driver.DrawPageAsync(source, [source]);

        var calls = rig.Calls.Where(call => call.Tool == "eplan_connection_create").ToArray();
        Assert.Equal([50, 50, 1], calls.Select(call => ((JArray)call.Args["items"]!).Count));
        Assert.All(calls, call => Assert.False((bool)call.Args["generate"]!));
        Assert.Single(rig.Calls, call => call.Tool == "eplan_connection_generate");
        Assert.Contains(((JArray)rig.Report.ToJson()["warnings"]!).OfType<JObject>(),
            warning => ((string?)warning["reason"])?.Contains("SymbolReference 102", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Report_GroupsMultilingualFieldsPerObjectAndKeepsRawDifferences()
    {
        JObject source = Page(Text(1, "Source"), Text(2, "Source"));
        source["propertyNames"] = new JObject { ["20031"] = "Function text" };
        JObject target = Page(Text(11, "Target"), Text(12, "Target"));
        var report = new SmallProjectRedrawReport("test", "ScratchTest");

        report.AddPage("/1", source, target);

        JObject result = report.ToJson();
        JToken field = Assert.Single((JArray)result["differentFields"]!,
            item => (string?)item["field"] == "properties[id=20031,index=0].value");
        Assert.Equal(2, (int)field["count"]!);
        Assert.Equal("Function text", (string?)field["name"]);
        Assert.Contains("Function text", report.ToMarkdown(), StringComparison.Ordinal);
        Assert.Equal(4, ((JArray)result["pages"]![0]!["types"]!["Text"]!["differentObjects"]![0]!["fields"]!).Count);
    }

    [Fact]
    public void Report_ListsMissingDefaultPropertyIdsAndGroupStructureOutsideMatchingRate()
    {
        JObject source = Page(Text(1, "same"));
        source["propertyNames"] = new JObject { ["20031"] = "Function text" };
        source["groups"] = new JArray(new JObject { ["location"] = Point(4, 240), ["count"] = 2, ["types"] = new JObject { ["Text"] = 2 } });
        JObject target = (JObject)source.DeepClone();
        target["groups"]![0]!["count"] = 3;
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        report.MissingPropertyPlacement("/1", "ContextPropertyPlacement", "2", 20031, 0, "Default placement is absent.");
        report.MissingPropertyPlacement("/1", "ContextPropertyPlacement", "3", 20031, 1, "Default placement is absent.");
        report.MissingPropertyPlacement("/1", "ContextPropertyPlacement", "4", null, null, "Default placement is absent.");

        report.AddPage("/1", source, target);

        JObject result = report.ToJson();
        Assert.Equal(3, (int)result["missingPropertyPlacementCount"]!);
        Assert.Equal(2, (int)result["missingPropertyIds"]![0]!["count"]!);
        Assert.Equal("Function text", (string?)result["missingPropertyIds"]![0]!["name"]);
        Assert.Equal(1, (int)result["groupStructureDifferenceCount"]!);
        Assert.Equal(1d, (double)result["totals"]!["matchingRate"]!);
        Assert.Contains("Group structure differences (1)", report.ToMarkdown(), StringComparison.Ordinal);
        Assert.Contains("Missing default property placements (3)", report.ToMarkdown(), StringComparison.Ordinal);
    }

    private sealed class Rig
    {
        internal List<(string Tool, JObject Args)> Calls { get; } = [];
        internal SmallProjectRedrawReport Report { get; } = new("test", "ScratchTest");
        internal SmallProjectRedrawDriver Driver { get; }

        internal Rig()
        {
            Driver = new SmallProjectRedrawDriver((tool, args) =>
            {
                Calls.Add((tool, (JObject)args.DeepClone()));
                return Task.FromResult(new JObject { ["execution"] = "committed" });
            }, "ScratchTest", Path.GetTempPath(), Report);
        }
    }

    private static JObject Text(int oid, string value) => new()
    {
        ["type"] = "Text",
        ["oid"] = oid,
        ["location"] = Point(oid % 10, 20),
        ["properties"] = new JArray(new JObject
        {
            ["id"] = 20031,
            ["index"] = 0,
            ["value"] = new JObject { ["de_DE"] = value, ["en_US"] = value, ["fr_FR"] = value, ["zh_CN"] = value },
        }),
    };

    private static JObject Node(int oid, double x, double y, params string[] directions) => new()
    {
        ["type"] = "SymbolReference",
        ["oid"] = oid,
        ["location"] = Point(x, y),
        ["connectionPoints"] = new JArray(directions.Select(direction => new JObject { ["direction"] = direction })),
    };

    private static JObject Page(params JObject[] placements) => new()
    {
        ["name"] = "/1",
        ["pageType"] = "Circuit",
        ["placements"] = new JArray(placements),
        ["connections"] = new JArray(),
    };

    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
}
