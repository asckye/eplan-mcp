using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests;

[Trait("Size", "Small")]
public sealed class PageExportRoundFourTests
{
    [Fact]
    public void Compare_LiveBlockReferencePointChange_PreservesAbsoluteMemberGeometry()
    {
        // Reduced native failure: the same member was exported relative to two different block reference points.
        const string source = """
            {"placements":[{"type":"Block","name":"144_268773","location":{"x":4.0,"y":240.0},
            "boundingBox":[{"x":3.5,"y":119.5},{"x":16.65,"y":240.610772743857}],
            "properties":[{"id":19002,"index":0,"value":"4"},{"id":19003,"index":0,"value":"240"}],"members":[
            {"type":"Arc","kind":"Circle","location":{"x":8.475,"y":-66.708085043303},
            "center":{"x":8.475,"y":-66.708085043303},"radius":2.0,"startAngle":0.0,"endAngle":0.0,
            "boundingBox":[{"x":6.475,"y":-68.708085043303},{"x":10.475,"y":-64.708085043303}],
            "properties":[{"id":19103,"index":0,"value":"X: 8.48 mm    Y: -66.71 mm"},
            {"id":19321,"index":0,"value":"8.475"},{"id":19322,"index":0,"value":"-66.708085043303"},
            {"id":19324,"index":0,"value":"0"}]}]}]}
            """;
        const string target = """
            {"placements":[{"type":"Block","name":"144_268773","location":{"x":3.75,"y":240.610772743857},
            "boundingBox":[{"x":3.5,"y":119.5},{"x":16.65,"y":240.610772743857}],
            "properties":[{"id":19002,"index":0,"value":"3.75"},{"id":19003,"index":0,"value":"240.610772743857"}],"members":[
            {"type":"Arc","kind":"Circle","location":{"x":8.725,"y":-67.31885778716003},
            "center":{"x":8.725,"y":-67.31885778716003},"radius":2.0,"startAngle":0.0,"endAngle":0.0,
            "boundingBox":[{"x":6.725,"y":-69.31885778716003},{"x":10.725,"y":-65.31885778716003}],
            "properties":[{"id":19103,"index":0,"value":"X: 8.73 mm    Y: -67.32 mm"},
            {"id":19321,"index":0,"value":"8.725"},{"id":19322,"index":0,"value":"-67.31885778716"},
            {"id":19324,"index":0,"value":"360"}]}]}]}
            """;

        PageExportComparison comparison = PageExportComparer.Compare(source, target);

        Assert.False(comparison.HasDifferences);
        Assert.True(comparison.IsComplete);
        Assert.Single(comparison.Types["Block"].Matched);
        Assert.Equal(4.0, (double)comparison.Types["Block"].Matched[0].Source["location"]!["x"]!);
    }

    [Theory]
    [InlineData("boundingBox", "boundingBox[0].x")]
    [InlineData("member", "members[0].end.x")]
    [InlineData("property", "members[0].properties[id=19024,index=0].value")]
    public void Compare_BlockOriginChange_DoesNotHideActualContentDifferences(string change, string expected)
    {
        const string json = """
            {"placements":[{"type":"Block","location":{"x":10,"y":20},"boundingBox":[{"x":10,"y":20}],
            "members":[{"type":"Line","location":{"x":0,"y":0},"start":{"x":0,"y":0},"end":{"x":2,"y":3},
            "properties":[{"id":19024,"index":0,"value":"Original"}]}]}]}
            """;
        JObject target = JObject.Parse(json);
        if (change == "boundingBox")
        { target["placements"]![0]!["boundingBox"]![0]!["x"] = 11; }
        else if (change == "member")
        { target["placements"]![0]!["members"]![0]!["end"]!["x"] = 3; }
        else
        { target["placements"]![0]!["members"]![0]!["properties"]![0]!["value"] = "Changed"; }

        PageExportComparison comparison = PageExportComparer.Compare(JObject.Parse(json), target);

        Assert.Equal(expected, Assert.Single(Assert.Single(comparison.Types["Block"].Different).Fields).Field);
    }

    [Fact]
    public void Compare_NestedBlockMembers_UseAccumulatedAbsoluteOrigins()
    {
        const string source = """
            {"placements":[{"type":"Block","location":{"x":10,"y":20},"members":[
            {"type":"Block","location":{"x":2,"y":3},"members":[
            {"type":"Rectangle","location":{"x":4,"y":5},"lowerLeft":{"x":4,"y":5},"upperRight":{"x":6,"y":7}}]}]}]}
            """;
        const string target = """
            {"placements":[{"type":"Block","location":{"x":11,"y":22},"members":[
            {"type":"Block","location":{"x":1,"y":1},"members":[
            {"type":"Rectangle","location":{"x":4,"y":5},"lowerLeft":{"x":4,"y":5},"upperRight":{"x":6,"y":7}}]}]}]}
            """;

        Assert.False(PageExportComparer.Compare(source, target).HasDifferences);
    }

    [Fact]
    public void Compare_InconsistentBlockReferenceProperty_RemainsADifference()
    {
        const string source = """
            {"placements":[{"type":"Block","location":{"x":4,"y":240},"members":[],
            "properties":[{"id":19002,"index":0,"value":"4"}]}]}
            """;
        const string target = """
            {"placements":[{"type":"Block","location":{"x":3.75,"y":240.61},"members":[],
            "properties":[{"id":19002,"index":0,"value":"4"}]}]}
            """;

        PageExportFieldDifference difference = Assert.Single(Assert.Single(PageExportComparer.Compare(source, target).Types["Block"].Different).Fields);

        Assert.Equal("properties[id=19002,index=0].value", difference.Field);
        Assert.Equal(0, (double)difference.Source!);
        Assert.Equal(0.25, (double)difference.Target!);
    }

    [Theory]
    [InlineData("startAngle", 0, 6.283185307179586)]
    [InlineData("endAngle", 0, 3.552713678800501e-15)]
    [InlineData("ellipseAngle", 1.5707963267948966, 1.5707968267948966)]
    [InlineData("angle", 6.28318530717958, 0)]
    public void Compare_AngleRoundoffAndWholeTurns_AreEquivalent(string field, double source, double target)
    {
        var left = new JObject { ["placements"] = new JArray(new JObject { ["type"] = "Arc", [field] = source }) };
        var right = (JObject)left.DeepClone();
        right["placements"]![0]![field] = target;

        Assert.False(PageExportComparer.Compare(left, right).HasDifferences);
    }

    [Fact]
    public void Compare_MultipleBlockArcs_WholeTurnsDoNotChangeMemberOrdering()
    {
        const string source = """
            {"placements":[{"type":"Block","location":{"x":4,"y":240},"members":[
            {"type":"Arc","location":{"x":0,"y":0},"center":{"x":0,"y":0},"radius":2,"startAngle":0,"endAngle":0},
            {"type":"Arc","location":{"x":0,"y":0},"center":{"x":0,"y":0},"radius":2,"startAngle":3.141592653589793,"endAngle":3.141592653589793}]}]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![0]!["members"]![0]!["startAngle"] = 2 * Math.PI;
        target["placements"]![0]!["members"]![0]!["endAngle"] = 3.552713678800501e-15;

        Assert.False(PageExportComparer.Compare(JObject.Parse(source), target).HasDifferences);
    }

    [Theory]
    [InlineData("19323", "0", "360")]
    [InlineData("19324", "0", "2.03555499613665E-13")]
    [InlineData("19342", "360", "0")]
    [InlineData("19343", "90", "90.0000005")]
    public void Compare_NumericAngleProperties_UseDegreeTurns(string id, string source, string target)
    {
        var left = JObject.Parse("""{"placements":[{"type":"Arc","properties":[{"id":0,"index":0,"value":""}]}]}""");
        left["placements"]![0]!["properties"]![0]!["id"] = int.Parse(id, System.Globalization.CultureInfo.InvariantCulture);
        left["placements"]![0]!["properties"]![0]!["value"] = source;
        var right = (JObject)left.DeepClone();
        right["placements"]![0]!["properties"]![0]!["value"] = target;

        Assert.False(PageExportComparer.Compare(left, right).HasDifferences);
    }

    [Theory]
    [InlineData(0, 0.000002)]
    [InlineData(-16002, -16002 + 2 * Math.PI)]
    public void Compare_RealAngleChangeAndLayerSentinel_RemainDifferences(double source, double target)
    {
        var left = new JObject { ["placements"] = new JArray(new JObject { ["type"] = "Text", ["angle"] = source }) };
        var right = (JObject)left.DeepClone();
        right["placements"]![0]!["angle"] = target;

        Assert.Equal("angle", Assert.Single(Assert.Single(PageExportComparer.Compare(left, right).Types["Text"].Different).Fields).Field);
    }

    [Fact]
    public void Compare_GroupOrder_IsIgnoredAndGroupCountsRemainPageDifferences()
    {
        const string source = """
            {"placements":[{"type":"Line","location":{"x":0,"y":0}}],"groups":[
            {"location":{"x":20,"y":5},"count":2,"types":{"Line":2}},
            {"location":{"x":10,"y":5},"count":1,"types":{"Rectangle":1}}]}
            """;
        JObject target = JObject.Parse(source);
        target["groups"] = new JArray(((JArray)target["groups"]!).Reverse().Select(group => group.DeepClone()));
        Assert.False(PageExportComparer.Compare(JObject.Parse(source), target).HasDifferences);
        target["groups"]![0]!["count"] = 2;
        target["groups"]![0]!["types"]!["Rectangle"] = 2;

        PageExportComparison comparison = PageExportComparer.Compare(JObject.Parse(source), target);

        Assert.Single(comparison.Types["Line"].Matched);
        Assert.Equal(["groups[0].count", "groups[0].types.Rectangle"], comparison.PageDifferences.Select(field => field.Field));
        Assert.Empty(comparison.SubsetDifferences);
    }

    [Theory]
    [InlineData("=GL1+A2&EFS1/1.3", 0, 1)]
    [InlineData("=K1+A2&EFS1/2.3", 1, 0)]
    public void Compare_ValuesFromAPointOnAnotherPage_AreSubsetDifferencesOnlyOutsideTheComparedPages(string reference, int different, int subset)
    {
        string Point(string partner, string text) => $$$"""
            {"placements":[{"type":"Terminal","oid":1,"location":{"x":1,"y":1},"properties":[{"id":20301,"index":0,"value":"{{{partner}}}"},{"id":20031,"index":0,"value":"{{{text}}}"}]},
            {"type":"ContextPropertyPlacement","oid":2,"parentOid":1,"propertyId":20031,"location":{"x":1,"y":1},"content":{"??_??":"{{{text}}}"}}]}
            """;
        HashSet<string> pages = ["=K1+A2&EFS1/2"];

        PageExportComparison comparison = PageExportComparer.Compare(Point(reference, "Start"), Point(reference, ""), pages);

        Assert.Equal(different, comparison.Types["Terminal"].Different.Count);
        Assert.Equal(different, comparison.Types["ContextPropertyPlacement"].Different.Count);
        Assert.Equal(subset == 0 ? 0 : 2, comparison.SubsetDifferences.Count);
    }

    [Fact]
    public void Compare_Blocks_PairByNameWhateverTheirOrder()
    {
        const string source = """{"placements":[{"type":"Block","name":"left","location":{"x":4,"y":240}},{"type":"Block","name":"right","location":{"x":216,"y":276}}]}""";
        const string target = """{"placements":[{"type":"Block","name":"right","location":{"x":216,"y":276}},{"type":"Block","name":"left","location":{"x":3,"y":240}}]}""";

        PageExportComparison comparison = PageExportComparer.Compare(source, target);

        Assert.Equal(2, comparison.Types["Block"].Matched.Count);
    }

    [Fact]
    public void Compare_BlockPropertyIds_AreProjectLocalSoOnlyValuesCount()
    {
        // Live 2026-11-04: the same value sits under 59459 in the sample and 59456 in the redraw.
        const string source = """{"placements":[{"type":"Line","location":{"x":1,"y":1},"properties":[{"id":59459,"index":0,"value":"37800 mm2"},{"id":59001,"index":0,"value":null}]}]}""";
        const string same = """{"placements":[{"type":"Line","location":{"x":1,"y":1},"properties":[{"id":59456,"index":0,"value":"37800 mm2"}]}]}""";
        const string other = """{"placements":[{"type":"Line","location":{"x":1,"y":1},"properties":[{"id":59456,"index":0,"value":"1 mm2"}]}]}""";

        Assert.False(PageExportComparer.Compare(source, same).HasDifferences);
        Assert.True(PageExportComparer.Compare(source, other).HasDifferences);
    }

    [Fact]
    public void Compare_ReportRows_AreCheckedByPositionAndTextWithoutCountingAsObjects()
    {
        const string source = """
            {"isReportPage":true,"placements":[
            {"type":"PlaceHolderText","location":{"x":10,"y":20},"content":{"??_??":"%QX2.0"}},
            {"type":"PlaceHolderText","location":{"x":10,"y":30},"content":{"??_??":"1.1"}},
            {"type":"Line","location":{"x":1,"y":1}}]}
            """;
        const string target = """
            {"isReportPage":true,"placements":[
            {"type":"PlaceHolderText","location":{"x":10,"y":20},"content":{"??_??":"%QX1.0"}},
            {"type":"PlaceHolderText","location":{"x":10,"y":30},"content":{"??_??":"1.1"}}]}
            """;

        PageExportComparison comparison = PageExportComparer.Compare(source, target);

        PageExportReportRows rows = comparison.ReportRows!;
        Assert.Equal(1, rows.Matched);
        Assert.Equal("(10, 20) ??_??@%QX2.0", Assert.Single(rows.SourceOnly));
        Assert.Equal("(10, 20) ??_??@%QX1.0", Assert.Single(rows.TargetOnly));
        Assert.Empty(comparison.Types);
    }

    [Fact]
    public void Compare_MissingGroups_AreReportedEvenOnReportPages()
    {
        const string source = """{"isReportPage":true,"groups":[{"location":{"x":1,"y":2},"count":1,"types":{"Text":1}}]}""";

        PageExportComparison comparison = PageExportComparer.Compare(source, """{"isReportPage":true,"groups":[]}""");

        Assert.Equal("groups[0]", Assert.Single(comparison.PageDifferences).Field);
        Assert.Empty(comparison.Types);
    }
}
