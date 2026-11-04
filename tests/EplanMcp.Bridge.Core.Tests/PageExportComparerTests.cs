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
    public void Compare_ObjectNumberString_IsIgnoredButOtherObjectPropertiesAreCompared()
    {
        const string source = """{"placements":[{"type":"Line","properties":[{"id":2000,"index":0,"value":"100"},{"id":19024,"index":0,"value":"Source description"}]}]}""";
        const string target = """{"placements":[{"type":"Line","properties":[{"id":2000,"index":0,"value":"900"},{"id":19024,"index":0,"value":"Target description"}]}]}""";

        PageExportFieldDifference difference = Assert.Single(Assert.Single(PageExportComparer.Compare(source, target).Types["Line"].Different).Fields);

        Assert.Equal("properties[id=19024,index=0].value", difference.Field);
        Assert.Contains(PageExportComparer.Ignored, rule => rule.Member == "2000" && rule.Reason.Contains("internal object ID", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_SubsetPageFields_AreListedWithNamesAndBothValuesWithoutReducingObjectMatch()
    {
        JObject source = JObject.Parse(LinePage);
        JObject target = JObject.Parse(LinePage);
        string[] ids = ["11004", "11040", "11041", "11043", "25020", "11067"];
        foreach (string id in ids)
        {
            source["properties"]![id] = "outside subset";
            target["properties"]![id] = "subset";
        }

        PageExportComparison result = PageExportComparer.Compare(source, target);

        Assert.False(result.HasDifferences);
        Assert.True(result.IsComplete);
        Assert.Single(result.Types["Line"].Matched);
        Assert.Equal(ids.Length, result.SubsetDifferenceCount);
        Assert.Equal(ids.Select(id => "properties." + id).Order(StringComparer.Ordinal), result.SubsetDifferences.Select(item => item.Field).Order(StringComparer.Ordinal));
        Assert.All(result.SubsetDifferences, difference =>
        {
            Assert.NotEmpty(difference.Name);
            Assert.NotEmpty(difference.Reason);
            Assert.Equal("outside subset", (string?)difference.Source);
            Assert.Equal("subset", (string?)difference.Target);
        });
    }

    [Fact]
    public void Compare_SubsetIndexedPageProperties_PreserveMissingExtraAndLanguageDifferences()
    {
        const string source = """{"properties":[{"id":11004,"index":0,"value":"3"},{"id":11067,"index":0,"value":{"en_US":"Original"}}]}""";
        const string target = """{"properties":[{"id":11067,"index":0,"value":{"en_US":"Subset"}},{"id":25020,"index":1,"value":"1"}]}""";

        PageExportComparison result = PageExportComparer.Compare(source, target);

        Assert.Empty(result.PageDifferences);
        Assert.Equal(3, result.SubsetDifferenceCount);
        Assert.Equal(["properties[id=11004,index=0].value", "properties[id=11067,index=0].value.en_US", "properties[id=25020,index=1].value"],
            result.SubsetDifferences.Select(item => item.Field));
        Assert.Null(result.SubsetDifferences[0].Target);
        Assert.Null(result.SubsetDifferences[2].Source);
    }

    [Fact]
    public void Compare_MissingReportAndPagePropertyCollections_StillListEverySubsetValue()
    {
        const string source = """{"isReportPage":true,"properties":{"11004":"12","11040":"next"},"report":[{"type":"TableOfContents","deviceTag":"first ... last"}]}""";

        PageExportComparison result = PageExportComparer.Compare(source, "{}");

        Assert.Equal(3, result.SubsetDifferenceCount);
        Assert.All(result.SubsetDifferences, difference => Assert.Null(difference.Target));
        Assert.Equal("report[0].type", Assert.Single(result.PageDifferences).Field);
    }

    [Fact]
    public void Compare_LiveAutomaticReportDescriptionAndDeviceRange_AreSubsetDifferences()
    {
        // Reduced live failure: a table-of-contents range contracts when only selected pages are redrawn.
        const string source = """
            {"isReportPage":true,"properties":{"11004":"12","11011":{"en_US":"Table of contents : &AAA1/1 - &EPA1/5","de_DE":"Inhaltsverzeichnis : &AAA1/1 - &EPA1/5"}},
             "report":[{"type":"TableOfContents","isAutomaticPageDescription":true,"deviceTag":"&AAA1/1 ... &EPA1/5"}]}
            """;
        const string target = """
            {"isReportPage":true,"properties":{"11004":"1","11011":{"en_US":"Table of contents : &AAA1/1 - =K1+A2&EFA1/5","de_DE":"Inhaltsverzeichnis : &AAA1/1 - =K1+A2&EFA1/5"}},
             "report":[{"type":"TableOfContents","isAutomaticPageDescription":true,"deviceTag":"&AAA1/1 ... =K1+A2&EFA1/5"}]}
            """;

        PageExportComparison result = PageExportComparer.Compare(source, target);

        Assert.False(result.HasDifferences);
        Assert.Equal(4, result.SubsetDifferenceCount);
        Assert.Contains(result.SubsetDifferences, item => item.Field == "report[0].deviceTag");
        Assert.Contains(result.SubsetDifferences, item => item.Field == "properties.11011.en_US");
        Assert.Contains(result.SubsetDifferences, item => item.Field == "properties.11011.de_DE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compare_OrdinaryAndManualReportDescriptions_RemainContentDifferences(bool reportPage)
    {
        var source = new JObject
        {
            ["isReportPage"] = reportPage,
            ["properties"] = new JObject { ["11011"] = "Source" },
            ["report"] = reportPage ? new JArray(new JObject { ["isAutomaticPageDescription"] = false }) : new JArray(),
        };
        var target = (JObject)source.DeepClone();
        target["properties"]!["11011"] = "Target";

        PageExportComparison result = PageExportComparer.Compare(source, target);

        Assert.Equal("properties.11011", Assert.Single(result.PageDifferences).Field);
        Assert.Empty(result.SubsetDifferences);
    }

    [Fact]
    public void Compare_DongleLicense_IsIgnored_ObjectScopedPageNumber_IsNotASubsetDependency()
    {
        const string source = """{"properties":{"10185":"license A"},"placements":[{"type":"Line","properties":[{"id":11004,"index":0,"value":"2"}]}]}""";
        const string target = """{"properties":{},"placements":[{"type":"Line","properties":[{"id":11004,"index":0,"value":"3"}]}]}""";

        PageExportComparison result = PageExportComparer.Compare(source, target);

        Assert.Empty(result.SubsetDifferences);
        Assert.Empty(result.PageDifferences);
        Assert.Single(result.Types["Line"].Different);
        Assert.DoesNotContain(PageExportComparer.SubsetFields, rule => rule.Member == "10185");
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
        Assert.Equal("properties[id=31004,index=0].value", Assert.Single(Assert.Single(PageExportComparer.Compare(JObject.Parse(source), target).Types["Connection"].Different).Fields).Field);
    }

    [Fact]
    public void Compare_LivePropertyArrayInsertionAndReordering_ReportsOnlyMissingAndExtraIds()
    {
        const string source = """
            {"placements":[{"type":"Function","properties":[
              {"id":20068,"index":0,"value":"0"},
              {"id":20121,"index":0,"value":"1"},
              {"id":20122,"index":0,"value":"1"},
              {"id":20123,"index":0,"value":"0"}
            ]}]}
            """;
        const string target = """
            {"placements":[{"type":"Function","properties":[
              {"id":20123,"index":0,"value":"0"},
              {"id":20051,"index":0,"value":"0"},
              {"id":20122,"index":0,"value":"1"},
              {"id":20121,"index":0,"value":"1"}
            ]}]}
            """;

        IReadOnlyList<PageExportFieldDifference> fields = Assert.Single(PageExportComparer.Compare(source, target).Types["Function"].Different).Fields;

        Assert.Equal(["properties[id=20068,index=0].value", "properties[id=20051,index=0].value"], fields.Select(f => f.Field));
        Assert.Equal("0", (string?)fields[0].Source);
        Assert.Null(fields[0].Target);
        Assert.Null(fields[1].Source);
        Assert.Equal("0", (string?)fields[1].Target);
    }

    [Fact]
    public void Compare_IndexedProperties_PairsByBothIdAndIndex()
    {
        const string source = """
            {"properties":[{"id":20022,"index":1,"value":"1"},{"id":20022,"index":2,"value":"2"}]}
            """;
        const string target = """
            {"properties":[{"id":20022,"index":2,"value":"changed"},{"id":20022,"index":1,"value":"1"}]}
            """;

        PageExportFieldDifference difference = Assert.Single(PageExportComparer.Compare(source, target).PageDifferences);

        Assert.Equal("properties[id=20022,index=2].value", difference.Field);
        Assert.Equal("2", (string?)difference.Source);
        Assert.Equal("changed", (string?)difference.Target);
    }

    [Fact]
    public void Compare_TargetOnlyArrayProperties_AreDifferences()
    {
        const string source = """{"properties":[{"id":11011,"index":0,"value":"Power"}]}""";
        const string target = """
            {"properties":[{"id":11012,"index":0,"value":"target only"},{"id":11011,"index":0,"value":"Power"}]}
            """;

        PageExportFieldDifference difference = Assert.Single(PageExportComparer.Compare(source, target).PageDifferences);
        Assert.Equal("properties[id=11012,index=0].value", difference.Field);
        Assert.Null(difference.Source);
        Assert.Equal("target only", (string?)difference.Target);
        Assert.Equal(2, PageExportComparer.Compare("{}", target).PageDifferences.Count);
    }

    [Fact]
    public void Compare_MissingPropertyArray_ReportsEachSourceValueInIdOrder()
    {
        const string source = """
            {"properties":[{"id":20022,"index":2,"value":"2"},{"id":11011,"index":0,"value":null},{"id":20022,"index":1,"value":"1"}]}
            """;

        PageExportComparison result = PageExportComparer.Compare(source, "{}");

        Assert.Equal(["properties[id=11011,index=0].value", "properties[id=20022,index=1].value", "properties[id=20022,index=2].value"],
            result.PageDifferences.Select(difference => difference.Field));
        Assert.All(result.PageDifferences, difference => Assert.Null(difference.Target));
    }

    [Fact]
    public void Compare_MultilingualPropertyValue_ReportsIdIndexAndLanguage()
    {
        const string source = """{"properties":[{"id":20011,"index":0,"value":{"en_US":"Source","de_DE":"Text"}}]}""";
        const string target = """{"properties":[{"id":20011,"index":0,"value":{"de_DE":"Text","en_US":"Target"}}]}""";

        PageExportFieldDifference difference = Assert.Single(PageExportComparer.Compare(source, target).PageDifferences);

        Assert.Equal("properties[id=20011,index=0].value.en_US", difference.Field);
        Assert.Equal("Source", (string?)difference.Source);
        Assert.Equal("Target", (string?)difference.Target);
    }

    [Theory]
    [InlineData("firstRadius", 2.5)]
    [InlineData("secondRadius", 2.5)]
    [InlineData("ellipseAngle", 1.5717963267948966)]
    public void Compare_LiveEllipseWithZeroCircleRadius_ReportsEllipseGeometry(string field, double value)
    {
        const string source = """
            {"placements":[{"type":"Arc","kind":"Ellipse","location":{"x":368.19053251362,"y":90.45769752315101},
            "center":{"x":368.19053251362,"y":90.45769752315101},"radius":0.0,
            "firstRadius":1.43748000000004,"secondRadius":1.43748000000004,"ellipseAngle":1.5707963267948966}]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![0]![field] = value;

        PageExportTypeComparison result = PageExportComparer.Compare(JObject.Parse(source), target).Types["Arc"];

        Assert.Empty(result.Missing);
        Assert.Empty(result.Extra);
        Assert.Equal(field, Assert.Single(Assert.Single(result.Different).Fields).Field);
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
              {"type":"Block","oid":1,"location":{"x":0,"y":0},"memberOids":[2,3],"members":[{"type":"Line","location":{"x":10,"y":0}},{"type":"Line","location":{"x":20,"y":0}}]},
              {"type":"Line","oid":2,"blockOid":1,"location":{"x":10,"y":0}},
              {"type":"Line","oid":3,"blockOid":1,"location":{"x":20,"y":0}}
            ]}
            """;
        const string target = """
            {"placements":[
              {"type":"Block","oid":100,"location":{"x":0,"y":0},"memberOids":[300,200],"members":[{"type":"Line","location":{"x":20,"y":0}},{"type":"Line","location":{"x":10,"y":0}}]},
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
              {"type":"Block","oid":1,"location":{"x":0,"y":0},"memberOids":[2],"members":[{"type":"Line","location":{"x":10,"y":0}}]},
              {"type":"Block","oid":4,"location":{"x":30,"y":0},"memberOids":[3],"members":[{"type":"Line","location":{"x":20,"y":0}}]},
              {"type":"Line","oid":2,"blockOid":1,"location":{"x":10,"y":0}},
              {"type":"Line","oid":3,"blockOid":4,"location":{"x":20,"y":0}}
            ]}
            """;
        JObject target = JObject.Parse(source);
        target["placements"]![0]!["memberOids"] = new JArray(3);
        target["placements"]![1]!["memberOids"] = new JArray(2);
        target["placements"]![0]!["members"]![0]!["location"]!["x"] = 20;
        target["placements"]![1]!["members"]![0]!["location"]!["x"] = 10;
        target["placements"]![2]!["blockOid"] = 4;
        target["placements"]![3]!["blockOid"] = 1;
        PageExportComparison result = PageExportComparer.Compare(JObject.Parse(source), target);
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Types["Block"].Different.Count);
        Assert.All(result.Types["Block"].Different, difference => Assert.Equal("members[0].location.x", Assert.Single(difference.Fields).Field));
        Assert.Equal(2, result.Types["Line"].Different.Count);
        Assert.All(result.Types["Line"].Different, difference => Assert.Equal("blockOid", Assert.Single(difference.Fields).Field));
    }

    [Fact]
    public void Compare_BlockWithoutDetachedMembers_PreservesEvidenceAndWarns()
    {
        const string source = """{"placements":[{"type":"Block","oid":1,"memberOids":[2]}]}""";
        const string target = """{"placements":[{"type":"Block","oid":100,"memberOids":[200]}]}""";
        PageExportComparison result = PageExportComparer.Compare(source, target);
        Assert.False(result.IsComplete);
        Assert.Equal(2, result.Warnings.Count);
        Assert.All(result.Warnings, warning => Assert.EndsWith(".members", warning.Path, StringComparison.Ordinal));
        PageExportObjectMatch match = Assert.Single(result.Types["Block"].Matched);
        Assert.Equal(2, (int)match.Source["memberOids"]![0]!);
        Assert.Equal(200, (int)match.Target["memberOids"]![0]!);
    }

    [Fact]
    public void Compare_BlockMemberIdsAreIrrelevant_WhenDetachedMemberContentMatches()
    {
        const string source = """{"placements":[{"type":"Block","oid":1,"memberOids":[2],"members":[{"type":"Line","oid":2,"location":{"x":4,"y":5},"pen":{"color":3},"layer":42}]}]}""";
        const string target = """{"placements":[{"type":"Block","oid":9,"memberOids":[999,888],"members":[{"type":"Line","oid":22,"layer":42,"pen":{"color":3},"location":{"x":4,"y":5}}]}]}""";

        PageExportComparison result = PageExportComparer.Compare(source, target);

        Assert.False(result.HasDifferences);
        Assert.True(result.IsComplete);
    }

    [Theory]
    [InlineData("geometry", "members[0].end.x")]
    [InlineData("pen", "members[0].pen.color")]
    [InlineData("layer", "members[0].layer")]
    [InlineData("text", "members[1].content.en_US")]
    public void Compare_BlockMembers_CompareGeometryAndStyleAfterSorting(string change, string expectedField)
    {
        const string source = """
            {"placements":[{"type":"Block","members":[
              {"type":"Text","location":{"x":6,"y":7},"content":{"en_US":"Text","de_DE":"Text"},"angle":0.5},
              {"type":"Line","location":{"x":4,"y":5},"start":{"x":4,"y":5},"end":{"x":8,"y":9},"pen":{"color":3},"layer":42}
            ]}]}
            """;
        JObject target = JObject.Parse(source);
        JArray members = (JArray)target["placements"]![0]!["members"]!;
        target["placements"]![0]!["members"] = new JArray(members.Reverse().Select(item => item.DeepClone()));
        members = (JArray)target["placements"]![0]!["members"]!;
        switch (change)
        {
            case "geometry":
                members[0]["end"]!["x"] = 9;
                break;
            case "pen":
                members[0]["pen"]!["color"] = 4;
                break;
            case "layer":
                members[0]["layer"] = 43;
                break;
            case "text":
                members[1]["content"]!["en_US"] = "Changed";
                break;
        }

        PageExportComparison result = PageExportComparer.Compare(JObject.Parse(source), target);

        Assert.True(result.IsComplete);
        Assert.Equal(expectedField, Assert.Single(Assert.Single(result.Types["Block"].Different).Fields).Field);
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
              {"type":"Block","oid":1,"location":{"x":0,"y":0},"memberOids":[2,3],"members":[]},
              {"type":"Function","oid":2,"blockOid":1,"name":"-A","location":{"x":10,"y":0}},
              {"type":"ContextPropertyPlacement","oid":3,"blockOid":1,"parentOid":2,"sourceOid":2,"propertyId":20010,"location":{"x":11,"y":1}}
            ]}
            """;
        const string target = """
            {"placements":[
              {"type":"Block","oid":10,"location":{"x":0,"y":0},"memberOids":[30,20],"members":[]},
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
