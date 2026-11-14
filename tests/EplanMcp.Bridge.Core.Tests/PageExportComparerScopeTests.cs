using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// Fields that name where an object came from or where else it is placed, rather than what is drawn on the compared
/// page. Live values of 2026-11-14 from the sixteen-page redraw.
/// </summary>
[Trait("Size", "Small")]
public sealed class PageExportComparerScopeTests
{
    private const string Page = """
        {"name":"=K1+A2&EFS1/1","pageType":"Circuit","placements":[
          {"type":"Function","oid":1,"name":"+A1-WE1","location":{"x":10,"y":20},"properties":[
            {"id":20468,"index":0,"value":"PXC\\2703981.ema"}
          ]}
        ],"connections":[]}
        """;

    private static JObject Source() => JObject.Parse(Page);

    private static JObject Target(params (int Id, int Index, object? Value)[] properties)
    {
        JObject target = JObject.Parse(Page);
        var list = new JArray();
        foreach ((int id, int index, object? value) in properties)
        {
            list.Add(new JObject { ["id"] = id, ["index"] = index, ["value"] = value is null ? null : JToken.FromObject(value) });
        }
        target["placements"]![0]!["properties"] = list;
        return target;
    }

    private static void AddSourceProperty(JObject page, int id, int index, string value) =>
        ((JArray)page["placements"]![0]!["properties"]!).Add(new JObject { ["id"] = id, ["index"] = index, ["value"] = value });

    [Fact]
    public void MacroOfOrigin_WhenBothNameAMacroFile_IsAMacroVersionDifference()
    {
        PageExportComparison result = PageExportComparer.Compare(Source(), Target((20468, 0, "PXC\\2703994.ema")));

        Assert.Single(result.Types["Function"].Matched);
        Assert.Empty(result.Types["Function"].Different);
        PageExportMacroNameDifference macro = Assert.Single(result.MacroNameDifferences);
        Assert.Equal("Function", macro.Type);
        Assert.Equal("PXC\\2703981.ema", macro.SourceMacro);
        Assert.Equal("PXC\\2703994.ema", macro.TargetMacro);
    }

    [Fact]
    public void MacroOfOrigin_WhenTheObjectWasNotDrawnFromAMacro_StaysADifference()
    {
        PageExportComparison result = PageExportComparer.Compare(Source(), Target());

        Assert.Empty(result.MacroNameDifferences);
        Assert.Equal("properties[id=20468,index=0].value",
            Assert.Single(Assert.Single(result.Types["Function"].Different).Fields).Field);
    }

    [Fact]
    public void MacroOfOrigin_DoesNotExcuseAnotherFieldOfTheSameObject()
    {
        PageExportComparison result = PageExportComparer.Compare(Source(),
            Target((20468, 0, "PXC\\2703994.ema"), (20031, 0, "Feeder")));

        Assert.Single(result.MacroNameDifferences);
        Assert.Equal("properties[id=20031,index=0].value",
            Assert.Single(Assert.Single(result.Types["Function"].Different).Fields).Field);
    }

    [Fact]
    public void Placement_OnAMountingPanel_IsOutsideTheRedrawnPages()
    {
        JObject source = Source();
        AddSourceProperty(source, 20477, 3, "+A1 (A1.S1)");

        PageExportComparison result = PageExportComparer.Compare(source, Target((20468, 0, "PXC\\2703981.ema")));

        Assert.Single(result.Types["Function"].Matched);
        Assert.Equal("Function 1: properties[id=20477,index=3].value", Assert.Single(result.SubsetDifferences).Field);
    }

    [Fact]
    public void Placement_OnAPageOutsideTheSubset_IsOutsideTheRedrawnPages()
    {
        JObject source = Source();
        AddSourceProperty(source, 20477, 2, "=K1+A2&EFA1/7.5");

        PageExportComparison result = PageExportComparer.Compare(source, Target((20468, 0, "PXC\\2703981.ema")),
            new HashSet<string>(StringComparer.Ordinal) { "=K1+A2&EFS1/1" });

        Assert.Single(result.Types["Function"].Matched);
        Assert.Equal("Function 1: properties[id=20477,index=2].value", Assert.Single(result.SubsetDifferences).Field);
    }

    [Fact]
    public void Placement_OnAComparedPage_StaysADifference()
    {
        JObject source = Source();
        AddSourceProperty(source, 20477, 2, "=K1+A2&EFS1/1.5");

        PageExportComparison result = PageExportComparer.Compare(source, Target((20468, 0, "PXC\\2703981.ema")),
            new HashSet<string>(StringComparer.Ordinal) { "=K1+A2&EFS1/1" });

        Assert.Empty(result.SubsetDifferences);
        Assert.Equal("properties[id=20477,index=2].value",
            Assert.Single(Assert.Single(result.Types["Function"].Different).Fields).Field);
    }

    [Fact]
    public void ObjectIdentificationAndPropertyOrigin_AreNotDrawingContent()
    {
        JObject source = Source();
        AddSourceProperty(source, 31160, 0, "28/17/27226/0");
        AddSourceProperty(source, 31161, 0, "28/17/28310/0");
        AddSourceProperty(source, 31047, 31005, "9");

        PageExportComparison result = PageExportComparer.Compare(source,
            Target((20468, 0, "PXC\\2703981.ema"), (31160, 0, "177/17/199/0")));

        Assert.Single(result.Types["Function"].Matched);
        Assert.Empty(result.SubsetDifferences);
    }

    private const string PointPage = """
        {"name":"=GAA+A1&EFS1/1","pageType":"Circuit","placements":[
          {"type":"InterruptionPoint","oid":7,"name":"-1L1","location":{"x":376,"y":248},"properties":[]}
        ],"connections":[]}
        """;

    private static JObject Point(string counterpartPage, bool paired)
    {
        JObject page = JObject.Parse(PointPage);
        JObject point = (JObject)page["placements"]![0]!;
        point["counterpartReference"] = paired ? "=EA+A1-FB1:1" : "";
        var list = (JArray)point["properties"]!;
        foreach ((int id, string? value) in new (int, string?)[]
            { (24300, counterpartPage + ".0"), (24351, "=EA+A1-FB1:1"), (20930, paired ? "0" : "1") })
        { list.Add(new JObject { ["id"] = id, ["index"] = 0, ["value"] = paired ? value : null }); }
        list.Add(new JObject { ["id"] = 20201, ["index"] = 5, ["value"] = paired ? "-1L1-E1-/0" : "-1L1--/" });
        return page;
    }

    [Fact]
    public void InterruptionPoint_PairedWithAnOmittedPage_IsASubsetDifference()
    {
        PageExportComparison result = PageExportComparer.Compare(Point("=EA+A1&EFS1/1", true), Point("=EA+A1&EFS1/1", false),
            new HashSet<string>(StringComparer.Ordinal) { "=GAA+A1&EFS1/1" });

        Assert.Single(result.Types["InterruptionPoint"].Matched);
        Assert.Empty(result.Types["InterruptionPoint"].Different);
        Assert.Equal(5, result.SubsetDifferences.Count);
    }

    [Fact]
    public void InterruptionPoint_PairedWithAComparedPage_StaysADifference()
    {
        PageExportComparison result = PageExportComparer.Compare(Point("=GAA+A1&EFS1/2", true), Point("=GAA+A1&EFS1/2", false),
            new HashSet<string>(StringComparer.Ordinal) { "=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2" });

        // The paired flag (20930) reaches this fixture as a value against null, which the default channel takes;
        // the live report has both sides filled, so the rule still has to cover it.
        Assert.Empty(result.SubsetDifferences);
        Assert.Equal(4, Assert.Single(result.Types["InterruptionPoint"].Different).Fields.Count);
    }

    [Fact]
    public void InterruptionPoint_WithoutACrossReference_StaysADifference()
    {
        JObject source = Point("=EA+A1&EFS1/1", true);
        ((JArray)source["placements"]![0]!["properties"]!).First(property => (int?)property["id"] == 24300).Remove();
        JObject target = Point("=EA+A1&EFS1/1", false);
        ((JArray)target["placements"]![0]!["properties"]!).First(property => (int?)property["id"] == 24300).Remove();

        PageExportComparison result = PageExportComparer.Compare(source, target,
            new HashSet<string>(StringComparer.Ordinal) { "=GAA+A1&EFS1/1" });

        Assert.Empty(result.SubsetDifferences);
        Assert.NotEmpty(result.Types["InterruptionPoint"].Different);
    }

    /// <summary>A page whose device owns two property texts that carry the device's own object number (A138).</summary>
    private static JObject TextOwner(params string[] contents)
    {
        var texts = new JArray();
        for (int index = 0; index < contents.Length; index++)
        {
            texts.Add(new JObject
            {
                ["type"] = "ContextPropertyPlacement",
                ["oid"] = 396,
                ["parentOid"] = 396,
                ["location"] = new JObject { ["x"] = 10 + index, ["y"] = 30 },
                ["content"] = new JObject { ["en_US"] = contents[index] },
                ["properties"] = new JArray(),
            });
        }
        var placements = new JArray(new JObject
        {
            ["type"] = "Function",
            ["oid"] = 396,
            ["name"] = "+A1-WE1",
            ["location"] = new JObject { ["x"] = 10, ["y"] = 20 },
            ["properties"] = new JArray(),
        });
        foreach (JToken text in texts)
        { placements.Add(text); }
        return new JObject
        {
            ["name"] = "=K1+A2&EFS1/1",
            ["pageType"] = "Circuit",
            ["placements"] = placements,
            ["connections"] = new JArray(),
        };
    }

    [Fact]
    public void PropertyText_KeepsItsOwnersIdentity_WhenSeveralTextsShareItsObjectNumber()
    {
        PageExportComparison result = PageExportComparer.Compare(TextOwner("-WE1", "1,5"), TextOwner("-WE1", "1,5"));

        Assert.Equal(2, result.Types["ContextPropertyPlacement"].Matched.Count);
        Assert.Empty(result.Types["ContextPropertyPlacement"].Different);
        Assert.Single(result.Types["Function"].Matched);
    }
}
