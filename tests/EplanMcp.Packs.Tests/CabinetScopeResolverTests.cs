using System.Text.Json.Nodes;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class CabinetScopeResolverTests
{
    [Theory]
    [InlineData("""[{"location":"+A1"}]""", """[{"location":"+A10"}]""", false)]
    [InlineData("""[{"location":"+A1"}]""", """[{"location":"+A1"}]""", true)]
    [InlineData("""[{"location":"+A1"}]""", """[{"installation":"=AP","location":"+A1"}]""", true)]
    [InlineData("""[{"location":"+A1","installation":"=AP"}]""", """[{"location":"+A1","installation":"=AQ"}]""", false)]
    [InlineData("""[{"installation":"=AP"}]""", """[{"functionalAssignment":"==AP"}]""", true)]
    [InlineData("""[{"location":"+A1"}]""", """[{"installationNumber":"++A1"}]""", true)]
    [InlineData("""[{"documentType":"&EF"}]""", """[{"userDefined":"#EF"}]""", true)]
    [InlineData("""[{"location":"+A1"}]""", """[{"location":"+a1"}]""", false)]
    [InlineData("""[{"location":"+A1"}]""", """[{"location":"+A1.1"}]""", false)]
    [InlineData("""[{"location":"+A1"},{"location":"+A2"}]""", """[{"location":"+A2"}]""", true)]
    [InlineData("""[{"location":"+A1"},{"location":"+A2"}]""", """[{"location":"+A10"},{"location":"+A3"}]""", false)]
    [InlineData("""[{"location":"+A1"},{"location":"+A2"}]""", """[{"location":"+A3"},{"installation":"=AP","location":"+A2"}]""", true)]
    public void Overlaps_LiteralPrefixDimensions_AreSymmetric(string a, string b, bool expected)
    {
        CabinetBinding cabinetA = Cabinet("a", a), cabinetB = Cabinet("b", b);

        Assert.Equal(expected, CabinetScopeResolver.Overlaps(cabinetA, cabinetB));
        Assert.Equal(expected, CabinetScopeResolver.Overlaps(cabinetB, cabinetA));
    }

    [Fact]
    public void Overlaps_All_IntersectsExplicitAndAll()
    {
        CabinetBinding all = Cabinet("default", all: true), explicitCabinet = Cabinet("a1");

        Assert.True(CabinetScopeResolver.Overlaps(all, explicitCabinet));
        Assert.True(CabinetScopeResolver.Overlaps(explicitCabinet, all));
        Assert.True(CabinetScopeResolver.Overlaps(all, all));
    }

    [Fact]
    public void ParseDesignation_AllMarkers_AreSeparateCompleteSegments()
    {
        IReadOnlyDictionary<string, string> segments = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
            CabinetScopeResolver.ParseDesignation("==FA=AP++N+A1&EF#U-K1"));

        Assert.Equal(6, segments.Count);
        string[] markers = ["==", "=", "++", "+", "&", "#"];
        Assert.Equal(["==FA", "=AP", "++N", "+A1", "&EF", "#U"], markers.Select(s => segments[s]));
    }

    [Theory]
    [InlineData("installation", "=AP", "=AP+A1-K1", "==AP+A1-K1")]
    [InlineData("location", "+A1", "=AP+A1-K1", "=AP++A1-K1")]
    [InlineData("documentType", "&EF", "=AP+A1&EF/1", "=AP+A1#EF/1")]
    [InlineData("functionalAssignment", "==FA", "==FA=AP+A1-K1", "=FA+A1-K1")]
    [InlineData("installationNumber", "++N", "++N+A1-K1", "+N-K1")]
    [InlineData("userDefined", "#U", "#U+A1-K1", "&U+A1-K1")]
    public void CabinetOf_EachMarker_MatchesItsOwnDimension(string key, string value, string match, string other)
    {
        var resolver = new CabinetScopeResolver([Cabinet("a", $$"""[{"{{key}}":"{{value}}"}]""")]);

        Assert.Equal(["a"], resolver.CabinetOf(match));
        Assert.Empty(resolver.CabinetOf(other));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("K1")]
    [InlineData("=AP+A")]
    [InlineData("=AP+A10-K1")]
    [InlineData("=AP++A1-K1")]
    [InlineData("=AP+A1.1-K1")]
    [InlineData("=AP+a1-K1")]
    public void CabinetOf_NoMatchingFullSegment_IsUnassigned(string? designation)
    {
        var resolver = new CabinetScopeResolver([Cabinet("a1")]);

        Assert.Empty(resolver.CabinetOf(designation));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("+")]
    [InlineData("=AP+A1+A1-K1")]
    [InlineData("=AP+++A1-K1")]
    [InlineData("=AP+A1+X-K1")]
    [InlineData("=AP+A1-")]
    [InlineData("=AP+A1/")]
    [InlineData("=AP+A1-K1+A10-K2")]
    [InlineData("=AP+A1/1/+A10/2")]
    [InlineData("=AP+A1-K?")]
    [InlineData("=AP+A*")]
    [InlineData("=AP+A[0-9]")]
    [InlineData("=AP+A1-K1\n")]
    [InlineData("=AP+ A1-K1")]
    public void CabinetOf_UnprovenStructure_IsUnassignedEvenForAll(string? designation)
    {
        var resolver = new CabinetScopeResolver([Cabinet("default", all: true)]);

        Assert.Null(CabinetScopeResolver.ParseDesignation(designation));
        Assert.Empty(resolver.CabinetOf(designation));
    }

    [Theory]
    [InlineData("=AP+A1-K1", "a1")]
    [InlineData("=AP+A10-K1", "a10")]
    [InlineData("=OTHER+A1-X1:1", "a1")]
    [InlineData("=AP+A2-K1", null)]
    public void CabinetOf_PageLessFunction_UsesFullDesignation(string designation, string? expected)
    {
        var function = new { Name = designation, Page = (string?)null };
        var resolver = new CabinetScopeResolver([Cabinet("a1"), Cabinet("a10", """[{"location":"+A10"}]""")]);

        Assert.Null(function.Page);
        Assert.Equal(expected is null ? [] : new[] { expected }, resolver.CabinetOf(function.Name));
    }

    [Theory]
    [InlineData("-K1")]
    [InlineData("/1")]
    [InlineData("=AP+A10&EF/1")]
    public void CabinetOf_SoleDefaultAll_AssignsProvenDesignations(string designation)
    {
        var resolver = new CabinetScopeResolver([Cabinet("default", all: true)]);

        Assert.Equal(["default"], resolver.CabinetOf(designation));
    }

    [Fact]
    public void CabinetOf_MultipleMatchingPrefixesInOneCabinet_ReturnsOneId()
    {
        var resolver = new CabinetScopeResolver([Cabinet("a1", """[{"location":"+A1"},{"installation":"=AP","location":"+A1"}]""")]);

        Assert.Equal(["a1"], resolver.CabinetOf("=AP+A1-K1"));
    }

    [Fact]
    public void CabinetOf_OverlappingCabinets_ReturnsAllMatchesAsAmbiguous()
    {
        var resolver = new CabinetScopeResolver([Cabinet("a1"), Cabinet("ap", """[{"installation":"=AP"}]""")]);

        Assert.Equal(["a1", "ap"], resolver.CabinetOf("=AP+A1-K1"));
        Assert.Equal(["a1"], resolver.CabinetOf("=OTHER+A1-K1"));
    }

    [Fact]
    public void CabinetOf_ConnectionAcrossCabinets_DoesNotExpandMembership()
    {
        var resolver = new CabinetScopeResolver([Cabinet("a1"), Cabinet("a10", """[{"location":"+A10"}]""")]);
        var connection = new { From = "+A1-K1", To = "+A10-K2" };

        Assert.Equal(["a1"], resolver.CabinetOf(connection.From));
        Assert.Equal(["a10"], resolver.CabinetOf(connection.To));
        Assert.DoesNotContain("a1", resolver.CabinetOf(connection.To));
    }

    [Fact]
    public void CabinetOf_SourceMappingChanges_DoNotChangeSnapshot()
    {
        CabinetBinding cabinet = Cabinet("a1");
        var cabinets = new List<CabinetBinding> { cabinet };
        var resolver = new CabinetScopeResolver(cabinets);
        cabinet.Structure!["prefixes"]![0]!["location"] = "+A10";
        cabinets.Clear();

        Assert.Equal(["a1"], resolver.CabinetOf("+A1-K1"));
        Assert.Empty(resolver.CabinetOf("+A10-K1"));
    }

    [Fact]
    public void CabinetOf_LegacyCabinetWithoutMapping_IsUnassigned()
    {
        var resolver = new CabinetScopeResolver([new() { Id = "default" }]);

        Assert.Empty(resolver.CabinetOf("+A1-K1"));
    }

    internal static CabinetBinding Cabinet(string id, string prefixes = """[{"location":"+A1"}]""", bool all = false) => new()
    {
        Id = id,
        Values = [],
        Layers = [],
        Structure = all ? new JsonObject { ["all"] = true } : new JsonObject { ["prefixes"] = JsonNode.Parse(prefixes) },
    };
}
