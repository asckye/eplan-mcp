using System.Globalization;
using System.Text.Json;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ActionCatalogFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eplan-action-catalog-" + Guid.NewGuid().ToString("N"));
    private readonly ActionCatalogReader _reader;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ActionCatalogFileTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Bin"));
        Directory.CreateDirectory(Path.Combine(_root, "Cfg"));
        _reader = new(() => (Path.Combine(_root, "Bin"), "2027.0.1"));
    }

    [Fact]
    public void OfficialRows_JoinCommittedCatalogAndDocumentationByRowNameAndType()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EplanMcp.slnx")))
        { root = root.Parent; }
        Assert.NotNull(root);
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "catalog/eplan-2026.0.3/actions.json")));
        string[] table = [.. File.ReadLines(Path.Combine(root.FullName, "docs/eplan/api-actions.md")).Where(l => l.StartsWith("| ", StringComparison.Ordinal))];
        ActionCatalogItem[] items = ActionCatalogReader.Official();
        foreach (JsonElement row in catalog.RootElement.EnumerateArray())
        {
            string prefix = "| " + row.GetProperty("row").GetInt32().ToString(CultureInfo.InvariantCulture) + " | ";
            string line = Assert.Single(table, l => l.StartsWith(prefix, StringComparison.Ordinal));
            string name = row.GetProperty("action").GetString()!;
            Assert.Contains("`" + name + "`", line, StringComparison.Ordinal);
            string? type = row.GetProperty("type").GetString();
            if (type is not null)
            { Assert.Contains("`" + type + "`", line, StringComparison.Ordinal); }
            ActionCatalogItem item = Assert.Single(items, i => i.Name == name && i.Type == type);
            Assert.True(JsonElement.DeepEquals(row.GetProperty("parameters"), JsonSerializer.SerializeToElement(item.Parameters)));
            Assert.Contains("row " + row.GetProperty("row").GetRawText() + ";", item.Reference, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SyntheticFixture_IsReadAtRuntimeWithUiTextSearchVersionGapsAndStaleCursors()
    {
        string path = Path.Combine(_root, "Cfg", "MFTools.xml");
        await File.WriteAllTextAsync(path, """
            <Settings><LEV1 name="UsedActions"><Setting name="1"><Val>FixtureOne /MODE:1</Val></Setting>
            <Setting name="2"><Val>FixtureTwo</Val></Setting></LEV1>
            <Tab text="Fixture"><Group text="Edit"><Button action="FixtureOne" text="Synthetic label"/></Group></Tab></Settings>
            """, Token);
        var first = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(_reader, source: "ui", limit: 1, cancellationToken: Token));
        Assert.Equal("2027.0.1", first.GetProperty("installationVersion").GetString());
        Assert.Contains(first.GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("version differs", StringComparison.Ordinal));
        Assert.DoesNotContain(_root, first.GetRawText(), StringComparison.Ordinal);
        string cursor = first.GetProperty("nextCursor").GetString()!;
        Assert.Single(ApiToolTests.Data(await ActionCatalogTools.SearchAsync(_reader, text: "synthetic LABEL", tier: "C-candidate", source: "ui", cancellationToken: Token)).GetProperty("items").EnumerateArray());
        await File.WriteAllTextAsync(path, "<Settings><Button action=\"Changed\"/></Settings>", Token);
        ApiToolTests.AssertError(await ActionCatalogTools.SearchAsync(_reader, source: "ui", limit: 1, cursor: cursor, cancellationToken: Token));
    }

    [Theory]
    [InlineData("2026.0.3.25421", false)]
    [InlineData("2026.0.3.25422", true)]
    [InlineData("2026.0.3", true)]
    [InlineData("2027.0.1", true)]
    [InlineData(null, true)]
    public async Task Version_ReportsUnverifiedBuildsAndPreservesTheReportedVersion(string? version, bool mismatch)
    {
        var reader = new ActionCatalogReader(() => (Path.Combine(_root, "Bin") + Path.DirectorySeparatorChar, version));
        ActionCatalogData data = await reader.ReadAsync(Token);
        Assert.Equal(version, data.InstallationVersion);
        Assert.Equal(mismatch, data.Gaps.Any(g => g.Contains("version differs", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("<broken>")]
    [InlineData("<!DOCTYPE x [<!ENTITY leak SYSTEM 'file:///must-not-be-read'>]><Settings>&leak;</Settings>")]
    [InlineData("<Future><Unknown action=\"MustNotGuess\"/></Future>")]
    public async Task InvalidOrUnknownXml_ReturnsGapAndKeepsOfficialRows(string xml)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Cfg", "MFTools.xml"), xml, Token);
        ActionCatalogData data = await _reader.ReadAsync(Token);
        Assert.Equal(177, data.Items.Length);
        Assert.Contains(data.Gaps, g => g.Contains("MFTools.xml", StringComparison.Ordinal));
        Assert.DoesNotContain(data.Gaps, g => g.Contains(_root, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingDefinitions_ReportGapAndReadAdditionalMenuFiles()
    {
        var missing = await _reader.ReadAsync(Token);
        Assert.Contains(missing.Gaps, g => g.Contains("were not found", StringComparison.Ordinal));
        await File.WriteAllTextAsync(Path.Combine(_root, "Cfg", "FixtureMenu.xml"), "<Menu text=\"Test\"><MenuItem action=\"MenuFixture\" text=\"Menu label\"/></Menu>", Token);
        ActionCatalogItem row = Assert.Single((await _reader.ReadAsync(Token)).Items, r => r.Source == "ui");
        Assert.Equal("Test", row.Location);
        Assert.Equal("Menu label", row.UiText);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
