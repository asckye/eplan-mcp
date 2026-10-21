using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PartSearchOperationTests
{
    private readonly FakePartsDatabase _database = new();

    public PartSearchOperationTests()
    {
        _database.Content.Parts.AddRange(
        [
            Part("SIE.3RT2016-1BB41", "SIE", "3RT2016-1BB41", "3RT20161BB41", "Contactor, AC-3, 4 kW/400 V", macro: true),
            Part("SIE.3RH2122-1HB40", "SIE", "3RH2122-1HB40", "3RH21221HB40", "COUPLING CONTACTOR RELAY, 2NO+2NC", macro: true),
            Part("PXC.3031212", "PXC", "ST 2,5", "3031212", "Feed-through terminal block", macro: false),
            Part("SIE.3RH2122-1HB40", "SIE", "3RH2122-1HB40", "3RH21221HB40", "COUPLING CONTACTOR RELAY, variant 2", macro: false, variant: "2"),
        ]);
    }

    [Fact]
    public void Execute_MatchesPartNumberTypeOrderNumberOrDescription_IgnoringCase()
    {
        Assert.Equal(["SIE.3RH2122-1HB40", "SIE.3RH2122-1HB40"], Numbers(Run("3rh21")));
        Assert.Equal(["PXC.3031212"], Numbers(Run("st 2,5")));
        Assert.Equal(["SIE.3RT2016-1BB41"], Numbers(Run("3RT20161")));
        Assert.Equal(["SIE.3RH2122-1HB40", "SIE.3RH2122-1HB40", "SIE.3RT2016-1BB41"], Numbers(Run("CONTACTOR")));
        Assert.Empty(Numbers(Run("motor")));
    }

    [Fact]
    public void Execute_SortsByPartNumberThenVariant_AndKeepsEachVariant()
    {
        PartSearchData data = Run("SIE");

        Assert.Equal(
            [("SIE.3RH2122-1HB40", "1", true), ("SIE.3RH2122-1HB40", "2", false), ("SIE.3RT2016-1BB41", "1", true)],
            data.Items.Select(p => (p.PartNumber, p.Variant, p.HasMacro)));
    }

    [Fact]
    public void Execute_Manufacturer_IsTheWholeShortNameIgnoringCase()
    {
        Assert.Equal(4, Run(".3").Total);
        Assert.Equal(3, Run(".3", manufacturer: "sie").Total);
        Assert.Equal(0, Run(".3", manufacturer: "SI").Total);
        Assert.Equal(["PXC.3031212"], Numbers(Run(".3", manufacturer: " PXC ")));
    }

    [Fact]
    public void Execute_SlicesAndCountsAll_AndNamesTheDatabase()
    {
        PartSearchData data = Run("contactor", offset: 1, limit: 1);

        Assert.Equal((3, @"$(MD_PARTS)\EPLAN_parts.alk"), (data.Total, data.Database));
        Assert.Equal(["SIE.3RH2122-1HB40"], Numbers(data));
        Assert.Equal("2", data.Items[0].Variant);
    }

    [Fact]
    public void Execute_PassesTheLanguage_AndBlankIsNone()
    {
        Run("SIE", language: "zh_CN");
        Run("SIE", language: " ");

        Assert.Equal(["zh_CN", null], _database.Reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" x ")]
    public void Execute_AQueryShorterThanTwoCharacters_IsInvalidAndReadsNothing(string? query)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Run(query));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Contains("'query' must have 2 to 100 characters", error.Error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_database.Reads);
    }

    [Fact]
    public void Execute_AQueryLongerThan100Characters_IsInvalid()
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run(new string('x', 101))).Error.Reason);
        Assert.Equal(0, Run(new string('x', 100)).Total);
    }

    [Theory]
    [InlineData(@"$(MD_PARTS)\EPLAN_parts.alk", @"$(MD_PARTS)\EPLAN_parts.alk")]
    [InlineData("SQL01|EPLAN_Parts|0", "SQL01|EPLAN_Parts")]
    [InlineData("SQL01|EPLAN_Parts|2|engineer|secret", "SQL01|EPLAN_Parts")]
    public void ForModel_KeepsAFileAsItIs_AndNeverALogin(string database, string expected)
    {
        Assert.Equal(expected, PartSearchOperation.ForModel(database));
    }

    [Fact]
    public void Execute_ASqlServerDatabase_IsNamedWithoutItsLogin()
    {
        _database.Content.Database = "SQL01|EPLAN_Parts|2|engineer|secret";

        Assert.Equal("SQL01|EPLAN_Parts", Run("SIE").Database);
    }

    private PartSearchData Run(string? query, string? manufacturer = null, string? language = null, int offset = 0, int limit = 50) =>
        (PartSearchData)new PartSearchOperation(_database).Execute(new PartSearchArgs { Query = query, Manufacturer = manufacturer, Language = language, Offset = offset, Limit = limit })!;

    private static List<string> Numbers(PartSearchData data) => [.. data.Items.Select(p => p.PartNumber)];

    private static PartSearchItem Part(string number, string manufacturer, string type, string order, string description, bool macro, string variant = "1") => new()
    {
        PartNumber = number,
        Variant = variant,
        Manufacturer = manufacturer,
        TypeNumber = type,
        OrderNumber = order,
        ProductGroup = "ElectricalRelay",
        Description = description,
        HasMacro = macro,
    };
}
