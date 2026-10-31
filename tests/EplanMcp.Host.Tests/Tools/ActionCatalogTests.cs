using System.Text.Json;
using System.Xml.Linq;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ActionCatalogTests
{
    private static ActionCatalogReader Missing => new(() => (null, null));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Official_Preserves177RowsParametersExclusionsAndOnlyPublishedWhitelistEntries()
    {
        ActionCatalogItem[] rows = ActionCatalogReader.Official();
        Assert.Equal(177, rows.Length);
        Assert.All(rows, row => Assert.Equal("official", row.Source));
        Assert.Equal(177, rows.Select(r => r.Reference).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ActionRunCatalog.Entries.Keys.Order(StringComparer.Ordinal), rows.Where(r => r.Tier == "A").Select(r => r.WhitelistEntry!).Order(StringComparer.Ordinal));
        Assert.All(rows.Where(r => r.Tier == "A"), row => Assert.Contains("api-notes", row.VerificationReference, StringComparison.Ordinal));
        Assert.Equal("C-candidate", rows.Single(r => r.Name == "backup" && r.Type == "PROJECT").Tier);
        Assert.Equal("A", rows.Single(r => r.Name == "backup" && r.Type == "MASTERDATA").Tier);
        Assert.Equal("C-candidate", rows.Single(r => r.Name == "synchronize" && r.Type == "SINGLELINE").Tier);
        var excluded = rows.Single(r => r.Name == "ExecuteScript");
        Assert.Equal("excluded", excluded.Tier);
        Assert.NotNull(excluded.ExcludedReason);
        var sensitive = rows.Single(r => r.Name == "XPartsSetDataSourceAction").Parameters.Single(p => p.GetProperty("name").GetString() == "SqlPassword");
        Assert.True(sensitive.GetProperty("sensitive").GetBoolean());
    }

    [Fact]
    public void SyntheticXml_SkipsUnknownFieldsRetainsUiDataAndJoinsExactTypes()
    {
        ActionCatalogItem[] rows = ActionCatalogReader.Parse(XDocument.Parse("""
            <Settings><MOD name="MFTools"><LEV1 name="ActionCategory"><Setting name="1"><Val>LocalAction;123</Val></Setting></LEV1>
              <LEV1 name="Unknown"><Setting name="bad"><Val>NotAnAction</Val></Setting></LEV1>
              <LEV1 name="UsedActions"><Setting name="2"><Val>backup /TYPE:MASTERDATA /MDTYPE:ARTICLES</Val></Setting>
                <Setting name="3"><Val>backup /TYPE:PROJECT</Val></Setting><Setting name="4"><Val>ExecuteScript</Val></Setting></LEV1>
              <Tab text="Tools"><Group text="Layers"><Button action="changelayer" text="Layer visibility" future="skip"/></Group></Tab>
            </MOD></Settings>
            """), "MFTools.xml", Token);
        Assert.Equal(5, rows.Length);
        Assert.DoesNotContain(rows, r => r.Name is "NotAnAction" or "123");
        Assert.Null(rows.Single(r => r.Name == "LocalAction").Location);
        Assert.Equal("A", rows.Single(r => r.Type == "MASTERDATA").Tier);
        Assert.Equal("C-candidate", rows.Single(r => r.Type == "PROJECT").Tier);
        Assert.Equal("excluded", rows.Single(r => r.Name == "ExecuteScript").Tier);
        var layer = rows.Single(r => r.Name == "changelayer");
        Assert.Equal("Tools / Layers", layer.Location);
        Assert.Equal("Layer visibility", layer.UiText);
        Assert.Contains(layer.Parameters, p => p.GetProperty("name").GetString() == "PROJECTNAME");
    }

    [Fact]
    public async Task MissingInstallation_ReturnsOfficialDataAndExplicitGapWithoutMachinePaths()
    {
        JsonElement data = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(Missing, source: "ui", cancellationToken: Token));
        Assert.Empty(data.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("installationVersion").ValueKind);
        Assert.Contains("No unambiguous", data.GetProperty("gaps")[0].GetString(), StringComparison.Ordinal);
        var result = ApiToolTests.Data(await ActionCatalogTools.GetAsync(Missing, "backup", cancellationToken: Token));
        Assert.Equal(2, result.GetProperty("total").GetInt32());
        var error = ApiToolTests.AssertError(await ActionCatalogTools.GetAsync(Missing, "BACKUP", cancellationToken: Token), ErrorReason.ResourceNotFound);
        Assert.Contains("No unambiguous", error.GetProperty("metadata").GetProperty("gaps").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paging_FiltersIntersectAndCursorBindsToolNameFiltersAndLimit()
    {
        var reader = Missing;
        var names = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            JsonElement data = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(reader, source: "official", limit: 100, cursor: cursor, cancellationToken: Token));
            Assert.NotEmpty(data.GetProperty("items").EnumerateArray());
            foreach (JsonElement row in data.GetProperty("items").EnumerateArray())
            { Assert.True(names.Add(row.GetProperty("reference").GetString()!)); }
            cursor = data.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        Assert.Equal(177, names.Count);
        var first = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(reader, text: "BACK", source: "official", limit: 1, cancellationToken: Token));
        cursor = first.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        ApiToolTests.AssertError(await ActionCatalogTools.SearchAsync(reader, text: "backup", source: "official", limit: 1, cursor: cursor, cancellationToken: Token));
        ApiToolTests.AssertError(await ActionCatalogTools.SearchAsync(reader, text: "BACK", source: "official", limit: 2, cursor: cursor, cancellationToken: Token));
        ApiToolTests.AssertError(await ActionCatalogTools.GetAsync(reader, "backup", limit: 1, cursor: cursor, cancellationToken: Token));
        Assert.Single(ApiToolTests.Data(await ActionCatalogTools.SearchAsync(reader, text: "BACK", source: "official", tier: "A", cancellationToken: Token)).GetProperty("items").EnumerateArray());
        ApiToolTests.AssertError(await ActionCatalogTools.SearchAsync(reader, cursor: string.Empty, cancellationToken: Token));
    }

    [Fact]
    public async Task Validation_AggregatesAndDoesNotReadInstallation_CancellationPropagates()
    {
        var unread = new ActionCatalogReader(() => throw new InvalidOperationException("Must validate first"));
        var error = ApiToolTests.AssertError(await ActionCatalogTools.SearchAsync(unread, text: " ", tier: "B", source: "network", limit: 0, cancellationToken: Token));
        foreach (string word in new[] { "name/text", "tier", "source", "limit" })
        { Assert.Contains(word, error.GetProperty("message").GetString(), StringComparison.Ordinal); }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ActionCatalogTools.SearchAsync(unread, cancellationToken: cancelled.Token));
    }
}
