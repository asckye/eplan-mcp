using System.Text.Json;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ApiCatalogCoverageTests
{
    [Fact]
    public void ShippedIndex_ContainsEveryRegisteredOperationGroupOverloadAndDictionary()
    {
        string root = Root();
        ApiIndex index = ApiIndex.Load();
        using JsonDocument coverage = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "catalog/coverage.json")));
        ApiEntry[] operations = [.. index.Data.Entries.Where(e => e.Row.Kind != "data_dictionary")];
        Assert.Equal(4649, coverage.RootElement.GetProperty("layers").GetProperty("operation").GetProperty("registered").GetInt32());
        Assert.Equal(4649, operations.Length);
        JsonElement[] members = [.. Directory.GetFiles(Path.Combine(root, "catalog/eplan-2026.0.3/members"), "*.jsonl")
            .SelectMany(File.ReadLines).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => JsonSerializer.Deserialize<JsonElement>(l))
            .Where(m => m.GetProperty("layer").GetString() == "operation")];
        Assert.Equal(members.Select(m => m.TryGetProperty("group", out JsonElement g) ? g.GetString() : m.GetProperty("id").GetString())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), operations.Select(e => e.Row.Group).Order(StringComparer.Ordinal));
        Assert.Equal(members.Select(m => m.GetProperty("id").GetString()).Order(StringComparer.Ordinal),
            operations.SelectMany(e => e.Details.Where(d => d.Kind == "overload").Select(d => d.Id)).Order(StringComparer.Ordinal));
        Assert.Equal(69, index.Data.Entries.Count(e => e.Row.Kind == "data_dictionary"));
        var properties = File.ReadLines(Path.Combine(root, "catalog/eplan-2026.0.3/data-dictionary.jsonl"))
            .Select(l => JsonSerializer.Deserialize<JsonElement>(l)).GroupBy(p => p.GetProperty("object").GetString()!)
            .ToDictionary(g => "D:" + g.Key, g => g.Count(), StringComparer.Ordinal);
        Assert.All(index.Data.Entries.Where(e => e.Row.Kind == "data_dictionary"), e =>
            Assert.Equal(properties[e.Row.Group], Assert.Single(e.Details, d => d.Kind == "dictionary").PropertyCount));
        Assert.Equal(coverage.RootElement.GetProperty("eplanApi").GetString(), index.Data.CatalogVersion);
        foreach (JsonElement unit in coverage.RootElement.GetProperty("units").EnumerateArray().Where(u => u.GetProperty("layer").GetString() != "action"))
        {
            ApiEntry entry = Assert.Single(index.Data.Entries, e => e.Row.Group == unit.GetProperty("unit").GetString());
            Assert.Equal(unit.GetProperty("status").GetString(), entry.Row.Status);
            Assert.Equal(unit.GetProperty("tools").EnumerateArray().Select(t => t.GetString()), entry.Row.Tools);
        }
        JsonElement function = ApiToolTests.Data(ApiTools.Get(index, "D:Function", cancellationToken: TestContext.Current.CancellationToken));
        string typeId = function.GetProperty("entry").GetProperty("id").GetString()!;
        JsonElement alias = ApiToolTests.Data(ApiTools.Get(index, typeId, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(JsonElement.DeepEquals(function.GetProperty("entry"), alias.GetProperty("entry")));
        Assert.True(JsonElement.DeepEquals(function.GetProperty("items"), alias.GetProperty("items")));
        Assert.True(function.GetProperty("items")[0].GetProperty("propertyCount").GetInt32() > 0);
        Assert.All(operations, e =>
        {
            Assert.InRange(e.Row.Signature.Length, 1, 180);
            Assert.All(e.Details, d => Assert.InRange(ResultShaper.ResultTokens(JsonSerializer.Serialize(d, HostJson.Options)), 0, ResultShaper.MaxTokenBudget - 3000));
            Assert.NotEmpty(ApiToolTests.Data(ApiTools.Get(index, e.Row.Group, cancellationToken: TestContext.Current.CancellationToken)).GetProperty("items").EnumerateArray());
        });
        ApiEntry excluded = operations.First(e => e.Namespace == "Eplan.EplApi.Gui");
        Assert.Equal("excluded", excluded.Row.Status);
        Assert.NotNull(excluded.Row.ExcludedReason);
    }

    private static string Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
