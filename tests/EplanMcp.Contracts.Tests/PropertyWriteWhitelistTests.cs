namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class PropertyWriteWhitelistTests
{
    [Fact]
    public void ShippedWhitelist_NoVerifiedBatch_IsEmptyAndCannotBeExtended()
    {
        Assert.Empty(PropertyWriteWhitelist.Entries);
        var dictionary = Assert.IsAssignableFrom<IDictionary<(int, string, int, string), PropertyWriteEntry>>(PropertyWriteWhitelist.Entries);
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary.Add((2026, "Function", 20011, "Terminal"), new()));
        Assert.Empty(PropertyWriteWhitelist.Entries);
    }

    [Theory]
    [InlineData(2026, "Function", 20011, "Terminal")]
    [InlineData(2026, "Function", 20027, "Plc")]
    [InlineData(2026, "Function", 20024, "Cable")]
    [InlineData(2026, "Connection", 31002, "")]
    [InlineData(2026, "Page", 11011, "")]
    [InlineData(2026, "Project", 10013, "")]
    [InlineData(2026, "ArticleReference", 20481, "")]
    [InlineData(2026, "Function", 20022, "Terminal")]
    [InlineData(2025, "Function", 20011, "Terminal")]
    [InlineData(2027, "Function", 20011, "Terminal")]
    [InlineData(2026, "Page", 20011, "Terminal")]
    [InlineData(2026, "function", 20011, "Terminal")]
    [InlineData(2026, "Function", 20011, "*")]
    [InlineData(2026, "Function", 2000, "Terminal")]
    public void ShippedWhitelist_AnyCandidateVersionTypeOrCategory_GrantsNothing(int version, string objectType, int id, string category)
    {
        Assert.False(PropertyWriteWhitelist.Entries.TryGetValue((version, objectType, id, category), out PropertyWriteEntry? entry));
        Assert.Null(entry);
    }
}
