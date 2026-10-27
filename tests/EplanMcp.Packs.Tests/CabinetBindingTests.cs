using static EplanMcp.Packs.Tests.CabinetScopeResolverTests;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class CabinetBindingTests
{
    [Theory]
    [InlineData("""[{"location":"+A1"}]""")]
    [InlineData("""[{"installation":"=AP"}]""")]
    [InlineData("""[{"installation":"=AP","location":"+A1"}]""")]
    [InlineData("""[{"location":"+A2"},{"location":"+A1"}]""")]
    public void ReadBinding_PotentialOverlapWithoutPageEvidence_ReportsBothPointers(string prefixes)
    {
        ProjectBinding binding = Binding(Cabinet("a1"), Cabinet("other", prefixes));

        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(PackJson.Serialize(binding)));

        Assert.StartsWith("/cabinets/1/structure:", ex.Message, StringComparison.Ordinal);
        Assert.Contains("/cabinets/0/structure", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_OverlappingTypedBinding_ReportsInvalidArgumentWithoutValues()
    {
        ProjectBinding binding = Binding(Cabinet("a1"), Cabinet("other", """[{"installation":"=AP"}]"""));

        ConfigMergeResult result = ConfigMerger.Merge([], [], binding, 0);

        Assert.False(result.Ok);
        Assert.Equal(ConfigMerger.InvalidArgument, result.Reason);
        Assert.Equal("/cabinets/1/structure", result.Location);
        Assert.Contains("/cabinets/0/structure", result.Message, StringComparison.Ordinal);
        Assert.Empty(result.Values.EnumerateArray());
    }

    [Fact]
    public void ReadBinding_DisjointFullSegments_AreAccepted()
    {
        ProjectBinding binding = Binding(Cabinet("a1"), Cabinet("a10", """[{"location":"+A10"}]"""));

        Assert.Equal(2, PackJson.ReadBinding(PackJson.Serialize(binding)).Cabinets.Count);
    }

    [Theory]
    [InlineData("a1")]
    [InlineData("other")]
    public void ReadBinding_AllOnNonDefaultCabinet_IsRefused(string id)
    {
        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(PackJson.Serialize(Binding(Cabinet(id, all: true)))));

        Assert.StartsWith("/cabinets/0/structure:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadBinding_DefaultAllMustBeReplacedByExplicitMappingsWhenSplitting()
    {
        ProjectBinding initial = Binding(Cabinet("default", all: true));
        Assert.Single(PackJson.ReadBinding(PackJson.Serialize(initial)).Cabinets);
        ProjectBinding overlapping = initial with { Cabinets = [.. initial.Cabinets, Cabinet("a10", """[{"location":"+A10"}]""")] };

        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(PackJson.Serialize(overlapping)));

        Assert.StartsWith("/cabinets/0/structure:", ex.Message, StringComparison.Ordinal);
        ProjectBinding split = overlapping with { Cabinets = [Cabinet("default"), overlapping.Cabinets[1]] };
        var resolver = new CabinetScopeResolver(PackJson.ReadBinding(PackJson.Serialize(split)).Cabinets);
        Assert.Equal(["default"], resolver.CabinetOf("+A1-K1"));
        Assert.Equal(["a10"], resolver.CabinetOf("+A10-K1"));
        Assert.Empty(resolver.CabinetOf("+A2-K1"));
    }

    private static ProjectBinding Binding(params CabinetBinding[] cabinets) => ConfigMergerTests.Binding() with { Cabinets = cabinets };
}
