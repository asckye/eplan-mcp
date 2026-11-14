using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class TranslationKeywordSearchOperationTests
{
    private readonly FakeTranslationDatabase _database = new();

    public TranslationKeywordSearchOperationTests()
    {
        _database.Content.Keywords.AddRange(
        [
            Keyword("Motor", ("de_DE", "Motor"), ("zh_CN", "电动机")),
            Keyword("Motor protection switch", ("de_DE", "Motorschutzschalter")),
            Keyword("Terminal strip", ("de_DE", "Klemmenleiste"), ("zh_CN", "端子排")),
        ]);
    }

    [Fact]
    public void Execute_MatchesTheSourceText_IgnoringCase()
    {
        Assert.Equal(["Motor", "Motor protection switch"], Sources(Run("motor")));
        Assert.Equal(["Terminal strip"], Sources(Run("STRIP")));
        Assert.Empty(Sources(Run("cable")));
        Assert.Equal(["motor", "STRIP", "cable"], _database.Queries);
    }

    [Fact]
    public void Execute_Exact_KeepsOnlyTheWholeSourceText()
    {
        Assert.Equal(["Motor"], Sources(Run("Motor", exact: true)));
        Assert.Equal(["Motor"], Sources(Run("motor", exact: true)));
        Assert.Empty(Sources(Run("Motor protection", exact: true)));
    }

    [Fact]
    public void Execute_ReturnsTheTranslationsAndTheDatabaseLanguages_SortedByCode()
    {
        TranslationKeywordData data = Run("Motor", exact: true);

        Assert.Equal(["de_DE", "en_US", "zh_CN"], data.Languages);
        Assert.Equal(["de_DE", "zh_CN"], Assert.Single(data.Items).Translations.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("电动机", Assert.Single(data.Items).Translations["zh_CN"]);
    }

    [Fact]
    public void Execute_SortsBySourceTextAndSlices_WithTheTotalBeforeSlicing()
    {
        Assert.Equal(["Motor"], Sources(Run("motor", limit: 1)));
        Assert.Equal(["Motor protection switch"], Sources(Run("motor", offset: 1, limit: 1)));
        Assert.Equal(2, Run("motor", limit: 1).Total);
        Assert.Empty(Sources(Run("motor", offset: 2, limit: 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Execute_WithoutAQuery_IsInvalid(string? query)
    {
        BridgeErrorException invalid = Assert.Throws<BridgeErrorException>(() => Run(query));

        Assert.Equal(ErrorReason.InvalidArgument, invalid.Error.Reason);
        Assert.Empty(_database.Queries);
    }

    [Fact]
    public void Execute_WithTooLongAQuery_IsInvalid()
    {
        Assert.Equal(ErrorReason.InvalidArgument,
            Assert.Throws<BridgeErrorException>(() => Run(new string('x', TranslationKeywordSearchOperation.MaxQueryLength + 1))).Error.Reason);
    }

    [Fact]
    public void Execute_ASqlServerDatabase_IsNamedWithoutItsLogin()
    {
        _database.Content.Database = "SQL01|EPLAN_Translate|2|engineer|secret";

        Assert.Equal("SQL01|EPLAN_Translate", Run("Motor").Database);
    }

    [Fact]
    public void Execute_WhenTheDatabaseFails_KeepsTheAdaptersError()
    {
        _database.Failure = new BridgeErrorException(ErrorReason.CapabilityMissing, "SQL Server is not read.");

        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeErrorException>(() => Run("Motor")).Error.Reason);
    }

    private TranslationKeywordData Run(string? query, bool exact = false, int offset = 0, int limit = 50) =>
        (TranslationKeywordData)new TranslationKeywordSearchOperation(_database)
            .Execute(new TranslationKeywordArgs { Query = query, Exact = exact, Offset = offset, Limit = limit })!;

    private static List<string> Sources(TranslationKeywordData data) => [.. data.Items.Select(k => k.Source)];

    private static TranslationKeywordItem Keyword(string source, params (string Language, string Text)[] translations) => new()
    {
        Source = source,
        Translations = translations.ToDictionary(t => t.Language, t => t.Text, StringComparer.Ordinal),
    };
}
