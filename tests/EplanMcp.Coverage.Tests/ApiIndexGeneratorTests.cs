using System.IO.Compression;
using System.Text.Json;
using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiIndexGeneratorTests
{
    [Fact]
    public void Builder_UsesCoverageLevelsAndExclusionPrecedence_AndPreservesAllOverloads()
    {
        var catalog = new ApiCatalog();
        catalog.Units["O:N.T.F"] = new("O:N.T.F", "operation", "N", "T:N.T", "method", null);
        catalog.Units["O:N.T.Old"] = new("O:N.T.Old", "operation", "N", "T:N.T", "method", "deprecated");
        catalog.Units["O:N.Child.T.F"] = new("O:N.Child.T.F", "operation", "N.Child", "T:N.Child.T", "method", null);
        CoverageReport report = Report([new("O:N.T.F", "operation", "automated_test", true, [])],
            [new() { Match = "N:N", Status = "env_blocked", Condition = "No license", Source = "notes" }]);
        JsonElement[] members = JsonSerializer.Deserialize<JsonElement[]>("""
            [
              {"id":"M:N.T.F(System.Int32)","group":"O:N.T.F","kind":"method","type":"T:N.T","returns":"System.Boolean","params":[{"name":"n","type":"System.Int32"}],"summary":"Find text"},
              {"id":"M:N.T.F(System.String)","group":"O:N.T.F","kind":"method","type":"T:N.T","params":[{"name":"s","type":"System.String"}],"excluded":{"reason":"deprecated","rule":"obsolete"}},
              {"id":"M:N.T.Old","group":"O:N.T.Old","kind":"method","type":"T:N.T"},
              {"id":"M:N.Child.T.F","group":"O:N.Child.T.F","kind":"method","type":"T:N.Child.T"}
            ]
            """)!;
        using JsonDocument objects = JsonDocument.Parse("{}");
        ApiIndexData data = ApiIndexGenerator.Build(catalog, report, Mapping(), members, [], objects.RootElement);
        ApiEntry implemented = Assert.Single(data.Entries, e => e.Row.Group == "O:N.T.F");
        Assert.Equal("automated_test", implemented.Row.Status);
        Assert.True(implemented.Row.Partial);
        Assert.Equal(2, implemented.Details.Count(d => d.Kind == "overload"));
        Assert.Equal("System.Boolean", implemented.Details[0].ReturnType);
        Assert.Equal("n", Assert.Single(implemented.Details[0].Parameters!).Name);
        Assert.Contains("deprecated", implemented.Details[1].ExcludedReason, StringComparison.Ordinal);
        Assert.Equal("deprecated", Assert.Single(data.Entries, e => e.Row.Group == "O:N.T.Old").Row.ExcludedReason);
        Assert.Equal("env_blocked", Assert.Single(data.Entries, e => e.Row.Group == "O:N.Child.T.F").Row.Status);
        report = ExcludedReport();
        ApiIndexData excluded = ApiIndexGenerator.Build(catalog, report, Mapping(), members.Reverse().ToArray(), [], objects.RootElement);
        Assert.Equal("excluded", Assert.Single(excluded.Entries, e => e.Row.Group == "O:N.T.F").Row.Status);
        Assert.Equal(ApiIndexGenerator.Encode(excluded), ApiIndexGenerator.Encode(ApiIndexGenerator.Build(catalog, report, Mapping(), members, [], objects.RootElement)));

        static CoverageReport ExcludedReport() => Report([new("O:N.T.F", "operation", "live_verified", false, [])],
            [new() { Match = "T:N.T", Status = "excluded", Reason = "unsafe", Note = "Rejected", Source = "ADR" }]);
    }

    [Fact]
    public void EncodedIndex_RoundTripsWithoutDiskAndContainsNoTimestamp()
    {
        var data = new ApiIndexData([], new(StringComparer.Ordinal));
        string encoded = ApiIndexGenerator.Encode(data);
        Assert.Equal(encoded, ApiIndexGenerator.Encode(data));
        using var buffer = new MemoryStream(Convert.FromBase64String(encoded));
        using var gzip = new GZipStream(buffer, CompressionMode.Decompress);
        ApiIndexData roundTrip = JsonSerializer.Deserialize<ApiIndexData>(gzip, CoverageJson.Options)!;
        Assert.Empty(roundTrip.Entries);
        Assert.Empty(roundTrip.Evidence);
    }

    [Fact]
    public void Dictionary_PreservesCountTypeAliasesAndEvidenceReferences()
    {
        var catalog = new ApiCatalog();
        catalog.Units["D:Function"] = new("D:Function", "data_dictionary", "Function", null, null, null);
        JsonElement[] types = JsonSerializer.Deserialize<JsonElement[]>("""
            [
              {"id":"T:N.FunctionPropertyList","ns":"N"},
              {"id":"T:N.Function","ns":"N"}
            ]
            """)!;
        using JsonDocument objects = JsonDocument.Parse("""
            {"Function":{"propertyList":"T:N.FunctionPropertyList","enum":"T:N.Properties.Function","properties":12}}
            """);
        var mapping = new ToolMapping
        {
            EplanApi = "2026.0.3",
            Infrastructure = [],
            UnreachedGenerated = [],
            Tools = [new("eplan_property_get", "read", "host", [], ["T:N.FunctionPropertyList"], [],
                new("docs/tools/eplan_property_get.md", "Implemented", ["A1"], [new("ReaderTests", "Small", "tests/ReaderTests.cs")], []), [], null)],
        };
        ApiIndexData data = ApiIndexGenerator.Build(catalog, Report([new("D:Function", "data_dictionary", "live_verified", false, ["eplan_property_get"])], []),
            mapping, [], types, objects.RootElement, new Dictionary<string, string>(StringComparer.Ordinal) { ["A1"] = "docs/eplan/api-notes.md#a1-environment" });
        ApiEntry entry = Assert.Single(data.Entries);
        Assert.Equal("data_dictionary", entry.Row.Kind);
        Assert.Equal("live_verified", entry.Row.Status);
        Assert.Equal(12, Assert.Single(entry.Details).PropertyCount);
        Assert.Contains("T:N.Function", entry.Aliases);
        Assert.Contains("T:N.Properties.Function", entry.Aliases);
        Assert.Contains("T:N.FunctionPropertyList", entry.Aliases);
        Assert.Equal(["eplan_property_get"], entry.Row.Tools);
        ApiDetail[] evidence = data.Evidence["eplan_property_get"];
        Assert.Contains(evidence, e => e.Reference == "docs/tools/eplan_property_get.md");
        Assert.Contains(evidence, e => e.Reference == "tests/ReaderTests.cs#ReaderTests");
        Assert.Contains(evidence, e => e.Reference == "docs/eplan/api-notes.md#a1-environment");
    }

    private static ToolMapping Mapping() => new() { EplanApi = "2026.0.3", Tools = [], Infrastructure = [], UnreachedGenerated = [] };

    private static CoverageReport Report(UnitCoverage[] units, ApiRule[] rules) => new()
    {
        EplanApi = "2026.0.3",
        Catalog = "catalog/eplan-2026.0.3",
        SdkCatalog = true,
        ActionCatalog = false,
        Layers = [],
        Dependencies = new(0, 0),
        DictionaryProperties = new(0, 0),
        ActionNames = new(0, 0),
        OperationsByNamespace = [],
        UndocumentedByNamespace = [],
        DataDictionaryByObject = [],
        Tools = [],
        Units = units,
        Unmatched = [],
        OverlayRules = rules,
        Availability = [],
    };
}
