using EplanMcp.Host.Tools;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class ActionCatalogTests
{
    [Fact]
    public async Task LocalInstallation_ContainsOver900UiActionsAndPublishedWhitelistIsTierA()
    {
        var reader = new ActionCatalogReader();
        ActionCatalogData data = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(data.InstallationVersion);
        Assert.True(data.Items.Where(i => i.Source == "ui").Select(i => i.Name).Distinct(StringComparer.Ordinal).Count() > 900);
        var search = await ActionCatalogTools.SearchAsync(reader, source: "ui", limit: 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(true, search.IsError);
        Assert.True(search.StructuredContent!.Value.GetProperty("total").GetInt32() > 900);
        foreach (string entry in ActionRunCatalog.Entries.Keys)
        {
            Assert.Contains(data.Items, i => i.WhitelistEntry == entry && i.Tier == "A" && i.VerificationReference is not null);
        }
        var get = await ActionCatalogTools.GetAsync(reader, "backup", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(true, get.IsError);
        Assert.Contains(get.StructuredContent!.Value.GetProperty("items").EnumerateArray(), i =>
            i.GetProperty("type").GetString() == "MASTERDATA" && i.GetProperty("tier").GetString() == "A");
    }
}
