using System.Text.Json;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class SelectionCatalogTests
{
    private static readonly string[] _editMembers = ["SelectObjects", "ClearSelection", "OpenPageWithName", "ClosePage"];
    [Fact]
    public void CatalogTools_ExposeExactSelectionAndEditorMembers()
    {
        ApiIndex index = ApiIndex.Load();
        var details = new List<string>();
        foreach (string type in new[] { "SelectionSet", "Edit" })
        {
            JsonElement search = ApiToolTests.Data(ApiTools.Search(index, type: "Eplan.EplApi.HEServices." + type, limit: 100, cancellationToken: TestContext.Current.CancellationToken));
            foreach (JsonElement row in search.GetProperty("items").EnumerateArray())
            {
                string group = row.GetProperty("group").GetString()!;
                if (type == "Edit" && !_editMembers.Any(n => group.EndsWith("." + n, StringComparison.Ordinal)))
                { continue; }
                details.Add(ApiToolTests.Data(ApiTools.Get(index, group, limit: 100, cancellationToken: TestContext.Current.CancellationToken)).GetRawText());
            }
        }
        Assert.Contains(details, d => d.Contains("SelectObjects", StringComparison.Ordinal));
        string directory = Path.Combine(AppContext.BaseDirectory, "catalog-lookup");
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "selection.jsonl"), details);
    }
}
