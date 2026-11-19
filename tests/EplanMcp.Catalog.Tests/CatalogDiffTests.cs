namespace EplanMcp.Catalog.Tests;

/// <summary>The comparison of two catalog folders: hand-written catalog files in temporary folders, hence Medium.</summary>
[Trait("Size", "Medium")]
public sealed class CatalogDiffTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-catalog-diff-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void Ids_UnfoldsEnumValuesAndDataDictionaryProperties()
    {
        string folder = Catalog("a",
            types: ["{\"id\":\"T:N.Shade\",\"kind\":\"enum\",\"values\":{\"Dark\":\"2\",\"Light\":\"1\"}}"],
            members: ["{\"id\":\"M:N.Widget.Draw(System.String)\",\"kind\":\"method\"}"],
            properties: ["{\"object\":\"Widget\",\"name\":\"WIDGET_NAME\",\"value\":\"20001\"}"]);

        Assert.Equal(
            ["F:Eplan.EplApi.DataModel.Properties.Widget.WIDGET_NAME", "F:N.Shade.Dark", "F:N.Shade.Light", "M:N.Widget.Draw(System.String)", "T:N.Shade"],
            CatalogDiff.Ids(folder).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Compare_ListsEachSideSortedAndMarksTheIdsTheBridgeUses()
    {
        string newer = Catalog("newer",
            types: ["{\"id\":\"T:N.Shade\",\"kind\":\"enum\",\"values\":{\"Dark\":\"2\",\"Grey\":\"3\"}}"],
            members: ["{\"id\":\"M:N.Widget.Draw(System.String,System.String)\"}", "{\"id\":\"M:N.Widget.Close\"}"],
            properties: ["{\"object\":\"Widget\",\"name\":\"WIDGET_UNIT\"}"]);
        string older = Catalog("older",
            types: ["{\"id\":\"T:N.Shade\",\"kind\":\"enum\",\"values\":{\"Dark\":\"2\"}}"],
            members: ["{\"id\":\"M:N.Widget.Draw(System.String)\"}", "{\"id\":\"M:N.Widget.Close\"}"],
            properties: []);
        var used = new HashSet<string>(StringComparer.Ordinal) { "M:N.Widget.Draw(System.String,System.String)", "M:N.Widget.Close" };

        IReadOnlyList<CatalogDifference> differences = CatalogDiff.Compare(newer, older, used);

        Assert.Equal(
        [
            new(CatalogDiff.BaseOnly, "F:Eplan.EplApi.DataModel.Properties.Widget.WIDGET_UNIT", false),
            new(CatalogDiff.BaseOnly, "F:N.Shade.Grey", false),
            new(CatalogDiff.BaseOnly, "M:N.Widget.Draw(System.String,System.String)", true),
            new(CatalogDiff.OtherOnly, "M:N.Widget.Draw(System.String)", false),
        ], differences);
        Assert.Contains("base_only: 3 IDs, 1 used by the Bridge", CatalogDiff.Summary(differences), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_GivesATabSeparatedFileWithAHeaderAndLfLineEnds()
    {
        string path = Path.Combine(_root.FullName, "diff.tsv");

        CatalogDiff.Write([new(CatalogDiff.BaseOnly, "M:N.Widget.Close", true)], path);

        Assert.Equal("side\tid\tused_by_bridge\nbase_only\tM:N.Widget.Close\tyes\n", File.ReadAllText(path));
    }

    [Fact]
    public void Usages_ReadsTheKeysOfTheUsagesObject()
    {
        string path = Path.Combine(_root.FullName, "bridge-usage.json");
        File.WriteAllText(path, "{\"eplanApi\":\"x\",\"usages\":{\"M:N.Widget.Close\":[],\"A:XEsUndoAction\":[]}}");

        Assert.Equal(["A:XEsUndoAction", "M:N.Widget.Close"], CatalogDiff.Usages(path).Order(StringComparer.Ordinal));
    }

    private string Catalog(string name, string[] types, string[] members, string[] properties)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root.FullName, name)).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "members"));
        File.WriteAllLines(Path.Combine(folder, "types.jsonl"), types);
        File.WriteAllLines(Path.Combine(folder, "members", "N.jsonl"), members);
        File.WriteAllLines(Path.Combine(folder, "data-dictionary.jsonl"), properties);
        return folder;
    }
}
