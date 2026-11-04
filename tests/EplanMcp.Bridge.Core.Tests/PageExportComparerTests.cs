using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests;

[Trait("Size", "Small")]
public sealed class PageExportComparerTests
{
    private const string LinePage = """
        {"name":"+A1/1","pageType":"Circuit","properties":{"11011":"Power"},"placements":[
          {"type":"Line","oid":1,"location":{"x":10,"y":20},"start":{"x":10,"y":20},"end":{"x":30,"y":20},"pen":{"color":1}}
        ],"connections":[]}
        """;

    [Fact]
    public void Compare_IdenticalExports_MatchesEveryObject()
    {
        PageExportComparison result = PageExportComparer.Compare(LinePage, LinePage);
        Assert.False(result.HasDifferences);
        Assert.True(result.IsComplete);
        Assert.Single(result.Types["Line"].Matched);
    }

    [Fact]
    public void Compare_MissingObject_ReportsSourceObject()
    {
        PageExportComparison result = PageExportComparer.Compare(LinePage, WithoutPlacements());
        Assert.True(result.HasDifferences);
        Assert.Equal(1, (int)Assert.Single(result.Types["Line"].Missing)["oid"]!);
        Assert.Empty(result.Types["Line"].Extra);
    }

    [Fact]
    public void Compare_ExtraObject_ReportsTargetObject()
    {
        PageExportComparison result = PageExportComparer.Compare(WithoutPlacements(), LinePage);
        Assert.Equal(1, (int)Assert.Single(result.Types["Line"].Extra)["oid"]!);
        Assert.Empty(result.Types["Line"].Missing);
    }

    [Fact]
    public void Compare_ChangedGeometry_ReportsFieldAndBothValues()
    {
        JObject target = JObject.Parse(LinePage);
        target["placements"]![0]!["end"]!["x"] = 35;
        PageExportComparison result = PageExportComparer.Compare(JObject.Parse(LinePage), target);
        PageExportFieldDifference field = Assert.Single(Assert.Single(result.Types["Line"].Different).Fields);
        Assert.Equal("end.x", field.Field);
        Assert.Equal(30, (int)field.Source!);
        Assert.Equal(35, (int)field.Target!);
    }

    [Fact]
    public void Compare_WithinPositionTolerance_MatchesAcrossRoundingBoundary()
    {
        JObject source = JObject.Parse(LinePage);
        source["placements"]![0]!["location"]!["x"] = 10.004;
        JObject target = (JObject)source.DeepClone();
        target["placements"]![0]!["location"]!["x"] = 10.014;
        target["placements"]![0]!["start"]!["x"] = 10.01;
        Assert.False(PageExportComparer.Compare(source, target).HasDifferences);
    }

    [Fact]
    public void Compare_IgnoredSessionFields_DoNotHideContentChanges()
    {
        JObject source = JObject.Parse(LinePage);
        JObject target = JObject.Parse(LinePage);
        foreach (PageExportIgnoreRule rule in PageExportComparer.Ignored.Where(rule => int.TryParse(rule.Member, out _)))
        {
            source["properties"]![rule.Member] = "source session";
            target["properties"]![rule.Member] = "target session";
        }
        target["placements"]![0]!["oid"] = 99;
        source["projectPath"] = "source project";
        target["projectPath"] = "target project";
        Assert.False(PageExportComparer.Compare(source, target).HasDifferences);
        target["properties"]!["11023"] = "user-entered change date";
        Assert.Equal("properties.11023", Assert.Single(PageExportComparer.Compare(source, target).PageDifferences).Field);
    }

    [Fact]
    public void Compare_FunctionNameAndTextFormatting_ReportContentFields()
    {
        const string source = """
            {"placements":[
              {"type":"Terminal","oid":1,"location":{"x":0,"y":0},"symbol":{"library":"IEC","name":"X","variant":0},"name":"-X1"},
              {"type":"Text","oid":2,"location":{"x":5,"y":0},"content":{"en_US":"Source"},"height":2.5}
            ]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![0]!["name"] = "-X2";
        target["placements"]![1]!["content"]!["en_US"] = "Target";
        target["placements"]![1]!["height"] = 3;
        PageExportComparison result = PageExportComparer.Compare(JObject.Parse(source), target);
        Assert.Equal("name", Assert.Single(Assert.Single(result.Types["Terminal"].Different).Fields).Field);
        Assert.Equal(["content.en_US", "height"], Assert.Single(result.Types["Text"].Different).Fields.Select(f => f.Field));
    }

    [Fact]
    public void Compare_ChangedSymbol_DoesNotPairDifferentSymbols()
    {
        const string source = """{"placements":[{"type":"Function","location":{"x":0,"y":0},"symbol":{"library":"IEC","name":"X","variant":0}}]}""";
        JObject target = JObject.Parse(source);
        target["placements"]![0]!["symbol"]!["variant"] = 1;
        PageExportTypeComparison result = PageExportComparer.Compare(JObject.Parse(source), target).Types["Function"];
        Assert.Single(result.Missing);
        Assert.Single(result.Extra);
        Assert.Empty(result.Different);
    }

    [Fact]
    public void Compare_PropertyPlacement_UsesPairedParentAndComparesVisibility()
    {
        const string source = """
            {"placements":[
              {"type":"Function","oid":1,"location":{"x":0,"y":0},"name":"-X1"},
              {"type":"PropertyPlacement","oid":2,"parentOid":1,"sourceOid":1,"location":{"x":1,"y":2},"propertyId":20010,"propertyIndex":0,"height":2,"isVisible":true}
            ]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![0]!["oid"] = 100;
        target["placements"]![1]!["oid"] = 200;
        target["placements"]![1]!["parentOid"] = 100;
        target["placements"]![1]!["sourceOid"] = 100;
        Assert.False(PageExportComparer.Compare(JObject.Parse(source), target).HasDifferences);
        target["placements"]![1]!["isVisible"] = false;
        PageExportTypeComparison result = PageExportComparer.Compare(JObject.Parse(source), target).Types["PropertyPlacement"];
        Assert.Equal("isVisible", Assert.Single(Assert.Single(result.Different).Fields).Field);
    }

    [Fact]
    public void Compare_OverlappingToleranceCandidates_FindsOneToOneMatching()
    {
        const string source = """{"placements":[{"type":"Line","location":{"x":0.009,"y":0}},{"type":"Line","location":{"x":0,"y":0}}]}""";
        const string target = """{"placements":[{"type":"Line","location":{"x":0,"y":0}},{"type":"Line","location":{"x":0.018,"y":0}}]}""";
        PageExportTypeComparison result = PageExportComparer.Compare(source, target).Types["Line"];
        Assert.Equal(2, result.Matched.Count);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Extra);
    }

    [Fact]
    public void Compare_ReportPage_OnlyComparesReportAndPageProperties()
    {
        const string source = """{"isReportPage":true,"name":"source","properties":{"11011":"Parts"},"report":[{"type":"Parts","formName":"F01","filterSchemaData":"all"}],"placements":[{"type":"Text","content":{"en_US":"Generated row"}}]}""";
        JObject target = JObject.Parse(source);
        target["name"] = "target";
        target["placements"] = new JArray();
        Assert.False(PageExportComparer.Compare(JObject.Parse(source), target).HasDifferences);
        target["report"]![0]!["filterSchemaData"] = "selected";
        Assert.Equal("report[0].filterSchemaData", Assert.Single(PageExportComparer.Compare(JObject.Parse(source), target).PageDifferences).Field);
    }

    [Fact]
    public void Compare_ReadErrors_ReturnsWarningsWithoutRefusal()
    {
        JObject source = JObject.Parse(LinePage);
        source["placements"]![0]!["errors"] = JArray.Parse("""[{"member":"Line.Pen","error":"unreadable"}]""");
        source["unhandledTypes"] = JObject.Parse("""{"SpecialGraphic":1}""");
        PageExportComparison result = PageExportComparer.Compare(source, JObject.Parse(LinePage));
        Assert.False(result.HasDifferences);
        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void Compare_ConnectionPropertiesAndRemappedReferences_AreCompared()
    {
        const string source = """
            {"placements":[{"type":"Function","oid":1,"name":"-X1","location":{"x":0,"y":0}}],
             "connections":[{"oid":2,"start":{"oid":1,"name":"-X1","pinName":"1","pinIndex":0,"designation":"1"},"end":{"name":"-X2","pinName":"2","pinIndex":0,"designation":"2"},"properties":[{"id":31004,"index":0,"value":"RD"}]}]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![0]!["oid"] = 11;
        target["connections"]![0]!["oid"] = 22;
        target["connections"]![0]!["start"]!["oid"] = 11;
        Assert.False(PageExportComparer.Compare(JObject.Parse(source), target).HasDifferences);
        target["connections"]![0]!["properties"]![0]!["value"] = "BU";
        Assert.Equal("properties[0].value", Assert.Single(Assert.Single(PageExportComparer.Compare(JObject.Parse(source), target).Types["Connection"].Different).Fields).Field);
    }

    [Fact]
    public void Compare_ConnectionDirectionChange_PairsByDevicesAndPins()
    {
        const string source = """
            {"connections":[{"start":{"name":"-X1","pinName":"1","index":0,"pinIndex":0,"designation":"A","direction":"Left"},
            "end":{"name":"-X2","pinName":"2","pinIndex":1,"direction":"Right"}}]}
            """;
        JObject target = JObject.Parse(source);
        target["connections"]![0]!["start"]!["direction"] = "Right";
        target["connections"]![0]!["start"]!["designation"] = "B";
        PageExportTypeComparison group = PageExportComparer.Compare(JObject.Parse(source), target).Types["Connection"];
        Assert.Empty(group.Missing);
        Assert.Empty(group.Extra);
        Assert.Equal(["start.designation", "start.direction"], Assert.Single(group.Different).Fields.Select(field => field.Field));
    }

    [Fact]
    public void Compare_UnresolvedReference_WarnsThatIdentityWasNotVerified()
    {
        const string source = """
            {"placements":[{"type":"Function","oid":1,"location":{"x":0,"y":0},"name":"-X1",
            "parentFunction":{"oid":2,"name":"-Parent"},"functionsInside":[{"oid":3,"name":"-Child"}]}]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![0]!["parentFunction"]!["oid"] = 20;
        target["placements"]![0]!["functionsInside"]![0]!["oid"] = 30;
        PageExportComparison result = PageExportComparer.Compare(JObject.Parse(source), target);
        Assert.False(result.HasDifferences);
        Assert.False(result.IsComplete);
        Assert.Equal(4, result.Warnings.Count);
    }

    [Fact]
    public void Compare_KnownParentChanged_ReportsRelationshipDifference()
    {
        const string source = """
            {"placements":[
              {"type":"Function","oid":1,"name":"-A","location":{"x":0,"y":0}},
              {"type":"Function","oid":2,"name":"-B","location":{"x":5,"y":0}},
              {"type":"Function","oid":3,"name":"-C","location":{"x":10,"y":0},"parentFunction":{"oid":1,"name":"-A"}}
            ]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![2]!["parentFunction"]!["oid"] = 2;
        target["placements"]![2]!["parentFunction"]!["name"] = "-B";
        PageExportComparison result = PageExportComparer.Compare(JObject.Parse(source), target);
        Assert.True(result.IsComplete);
        Assert.Equal(["parentFunction.name", "parentFunction.oid"], Assert.Single(result.Types["Function"].Different).Fields.Select(field => field.Field));
    }

    [Fact]
    public void Compare_BlockMembersAndOwners_AreRemappedThroughPairedObjects()
    {
        const string source = """
            {"placements":[
              {"type":"Block","oid":1,"location":{"x":0,"y":0},"memberOids":[2,3]},
              {"type":"Line","oid":2,"blockOid":1,"location":{"x":10,"y":0}},
              {"type":"Line","oid":3,"blockOid":1,"location":{"x":20,"y":0}}
            ]}
            """;
        const string target = """
            {"placements":[
              {"type":"Block","oid":100,"location":{"x":0,"y":0},"memberOids":[300,200]},
              {"type":"Line","oid":200,"blockOid":100,"location":{"x":10,"y":0}},
              {"type":"Line","oid":300,"blockOid":100,"location":{"x":20,"y":0}}
            ]}
            """;
        PageExportComparison result = PageExportComparer.Compare(source, target);
        Assert.False(result.HasDifferences);
        Assert.True(result.IsComplete);
        Assert.Single(result.Types["Block"].Matched);
        Assert.Equal(2, result.Types["Line"].Matched.Count);
    }

    [Fact]
    public void Compare_BlockMembershipChange_IsNotHiddenByIdRemapping()
    {
        const string source = """
            {"placements":[
              {"type":"Block","oid":1,"location":{"x":0,"y":0},"memberOids":[2]},
              {"type":"Block","oid":4,"location":{"x":30,"y":0},"memberOids":[3]},
              {"type":"Line","oid":2,"blockOid":1,"location":{"x":10,"y":0}},
              {"type":"Line","oid":3,"blockOid":4,"location":{"x":20,"y":0}}
            ]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![0]!["memberOids"] = new JArray(3);
        target["placements"]![1]!["memberOids"] = new JArray(2);
        target["placements"]![2]!["blockOid"] = 4;
        target["placements"]![3]!["blockOid"] = 1;
        PageExportComparison result = PageExportComparer.Compare(JObject.Parse(source), target);
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Types["Block"].Different.Count);
        Assert.All(result.Types["Block"].Different, difference => Assert.Equal("memberOids[0]", Assert.Single(difference.Fields).Field));
        Assert.Equal(2, result.Types["Line"].Different.Count);
        Assert.All(result.Types["Line"].Different, difference => Assert.Equal("blockOid", Assert.Single(difference.Fields).Field));
    }

    [Fact]
    public void Compare_UnpairedBlockMembers_PreserveEvidenceAndWarn()
    {
        const string source = """{"placements":[{"type":"Block","oid":1,"memberOids":[2]}]}""";
        const string target = """{"placements":[{"type":"Block","oid":100,"memberOids":[200]}]}""";
        PageExportComparison result = PageExportComparer.Compare(source, target);
        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Warnings.Count);
        PageExportFieldDifference difference = Assert.Single(Assert.Single(result.Types["Block"].Different).Fields);
        Assert.Equal("memberOids[0]", difference.Field);
        Assert.Equal(2, (int)difference.Source!);
        Assert.Equal(200, (int)difference.Target!);
    }

    [Fact]
    public void Compare_ContextPropertyPlacement_RequiresThePairedParent()
    {
        const string source = """
            {"placements":[
              {"type":"Function","oid":1,"name":"-A","location":{"x":0,"y":0}},
              {"type":"Function","oid":2,"name":"-B","location":{"x":5,"y":0}},
              {"type":"ContextPropertyPlacement","oid":3,"parentOid":1,"sourceOid":1,"propertyId":20010,"location":{"x":1,"y":1}}
            ]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![2]!["parentOid"] = 2;
        PageExportTypeComparison result = PageExportComparer.Compare(JObject.Parse(source), target).Types["ContextPropertyPlacement"];
        Assert.Single(result.Missing);
        Assert.Single(result.Extra);
        Assert.Empty(result.Matched);
        Assert.Empty(result.Different);
    }

    [Fact]
    public void Compare_BlockWithContextPropertyPlacement_ResolvesEveryMember()
    {
        const string source = """
            {"placements":[
              {"type":"Block","oid":1,"location":{"x":0,"y":0},"memberOids":[2,3]},
              {"type":"Function","oid":2,"blockOid":1,"name":"-A","location":{"x":10,"y":0}},
              {"type":"ContextPropertyPlacement","oid":3,"blockOid":1,"parentOid":2,"sourceOid":2,"propertyId":20010,"location":{"x":11,"y":1}}
            ]}
            """;
        const string target = """
            {"placements":[
              {"type":"Block","oid":10,"location":{"x":0,"y":0},"memberOids":[30,20]},
              {"type":"Function","oid":20,"blockOid":10,"name":"-A","location":{"x":10,"y":0}},
              {"type":"ContextPropertyPlacement","oid":30,"blockOid":10,"parentOid":20,"sourceOid":20,"propertyId":20010,"location":{"x":11,"y":1}}
            ]}
            """;
        PageExportComparison result = PageExportComparer.Compare(source, target);
        Assert.True(result.IsComplete);
        Assert.False(result.HasDifferences);
        Assert.Single(result.Types["ContextPropertyPlacement"].Matched);
    }

    private static string WithoutPlacements()
    {
        JObject page = JObject.Parse(LinePage);
        page["placements"] = new JArray();
        return page.ToString();
    }
}
