using EplanMcp.Contracts;
using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;
using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage.Tests.Reporting;

[Trait("Size", "Medium")]
public sealed class GenericReadCoverageTests
{
    private static readonly string[] _genericOnly = ["D:Arc", "D:Comment", "D:Function3D", "D:TerminalStrip"];

    [Theory]
    [InlineData(null, false)]
    [InlineData("Large", false)]
    [InlineData("Small", true)]
    [InlineData("Medium", true)]
    public void Build_GenericReaders_CountSupportedDictionaryKinds_WithTestsAndExplicitLiveEvidence(string? size, bool tested)
    {
        using TempRepository repository = Create(size);

        CoverageReport report = CoverageReport.Build(repository.Root);
        ToolMapping mapping = ToolMapping.Build(repository.Root);
        LayerCount layer = report.Layers["data_dictionary"];
        Dictionary<string, UnitCoverage> units = report.Units.ToDictionary(u => u.Unit);

        Assert.Equal((7, 6, 5, 5), (layer.Registered, layer.Denominator, layer.Implemented, layer.ByTools));
        Assert.Equal((tested ? 5 : 1, tested ? 3 : 1), (layer.AutomatedTest, layer.LiveVerified));
        Assert.Equal("live_verified", units["D:Page"].Status); // Existing dedicated-reader evidence is retained.
        Assert.Equal(tested ? "live_verified" : "implemented", units["D:Arc"].Status); // A62 names Arc.
        Assert.Equal(tested ? "live_verified" : "implemented", units["D:TerminalStrip"].Status); // A60 / ObjectReadTests.
        Assert.Equal(tested ? "automated_test" : "implemented", units["D:Comment"].Status); // A62: zero objects.
        Assert.Equal(tested ? "automated_test" : "implemented", units["D:Function3D"].Status); // Cabinet does not verify its base kind.
        Assert.DoesNotContain("D:ArticleReference", units.Keys);
        Assert.DoesNotContain("D:Retired", units.Keys);
        Assert.Equal(1, layer.Excluded["deprecated"]);
        Assert.All(_genericOnly, key =>
            Assert.Equal(["eplan_object_find", "eplan_object_get"], units[key].Tools));
        Assert.All(mapping.Tools.Where(t => ToolMapping.IsObjectReader(t.Name)), tool =>
        {
            Assert.Equal(["D:Arc", "D:Comment", "D:Function3D", "D:Page", "D:TerminalStrip"], tool.DataDictionary);
            Assert.Equal(["T:Eplan.EplApi.Test.CommentPropertyList"], tool.Api);
            Assert.Equal(5, report.Tools.Single(t => t.Name == tool.Name).DataDictionary);
        });
        Assert.Equal(new UsedCount(1, 6), report.DictionaryProperties); // Generic units do not fabricate property references.
    }

    [Fact]
    public void Build_GenericLiveEvidence_DoesNotCarryToAnotherSdkBuild()
    {
        using TempRepository repository = Create("Small");
        repository.Write(BridgeUsage.RelativePath, repository.Read(BridgeUsage.RelativePath).Replace("2026.0.3.25421", "2026.0.3.99999", StringComparison.Ordinal));

        Assert.Equal("automated_test", CoverageReport.Build(repository.Root).Units.Single(u => u.Unit == "D:Arc").Status);
    }

    [Fact]
    public void Build_Repository_GenericMappingCoversEverySupportedDictionaryKind()
    {
        string root = Program.RepositoryRoot(AppContext.BaseDirectory)!;
        ApiCatalog catalog = ApiCatalog.Load(Path.Combine(root, "catalog/eplan-2026.0.3"));
        CoverageReport report = CoverageReport.Build(root);
        string[] expected = [.. catalog.Units.Values.Where(u => u.Layer == ApiCatalog.DataDictionary && ObjectReadTypes.Supported(u.Scope))
            .Select(u => u.Key).Order(StringComparer.Ordinal)];

        Assert.All(ToolMapping.Build(root).Tools.Where(t => ToolMapping.IsObjectReader(t.Name)), tool => Assert.Equal(expected, tool.DataDictionary));
        Assert.All(expected.Where(key => catalog.Units[key].Exclusion is null), key =>
        {
            UnitCoverage unit = Assert.Single(report.Units, u => u.Unit == key);
            Assert.Contains("eplan_object_find", unit.Tools);
            Assert.Contains("eplan_object_get", unit.Tools);
            Assert.True(unit.Status is "automated_test" or "live_verified");
        });
        Assert.Equal("transient_reference:A60", ObjectReadTypes.UnsupportedReason("ArticleReference"));
        Assert.Equal("unstable_identity:A62", ObjectReadTypes.UnsupportedReason("MateEntry"));
    }

    private static TempRepository Create(string? size)
    {
        TempRepository repository = ReportFixture.Create();
        repository.Write("tests/Contracts.Snapshots/server-contract.verified.json", """
            { "tools": [
              { "name": "eplan_page_create", "annotations": {} },
              { "name": "eplan_page_list", "annotations": { "readOnlyHint": true } },
              { "name": "eplan_object_find", "annotations": { "readOnlyHint": true } },
              { "name": "eplan_object_get", "annotations": { "readOnlyHint": true } }
            ] }
            """);
        repository.Write("src/EplanMcp.Host/Tools/ObjectReadTools.cs", """
            internal sealed class ObjectReadTools
            {
                [McpServerTool(Name = "eplan_object_find")]
                public static object Find() => Invoke(OperationNames.ObjectFind);
                [McpServerTool(Name = "eplan_object_get")]
                public static object Get() => Invoke(OperationNames.ObjectGet);
            }
            """);
        repository.Write("src/EplanMcp.Contracts/OperationNames.cs", repository.Read("src/EplanMcp.Contracts/OperationNames.cs")
            .Replace("public const string PageList", "public const string ObjectFind = \"object.find\", ObjectGet = \"object.get\";\n    public const string PageList", StringComparison.Ordinal));
        repository.Write(Overlay.RelativePath, """
            live:
              eplan_page_list:
                - { date: 2026-10-19, result: pass, basis: stated, source: api-notes A12 }
              eplan_object_find:
                - { date: 2026-10-31, eplan: 2026.0.3.25421, result: pass, basis: stated, source: api-notes A62 }
              eplan_object_get:
                - { date: 2026-10-31, eplan: 2026.0.3.25421, result: pass, basis: stated, source: api-notes A62 }
            """);
        // The shared list reference tests both scanner-derived and generic mapping statuses.
        BridgeUsage usage = CoverageJson.Read<BridgeUsage>(Path.Combine(repository.Root, BridgeUsage.RelativePath));
        usage.Usages["T:Eplan.EplApi.Test.CommentPropertyList"] = [ReportFixture.GeneratedSite("object.find", "object.get")];
        repository.Write(BridgeUsage.RelativePath, CoverageJson.Serialize(new BridgeUsage
        {
            EplanApi = usage.EplanApi,
            ScannedOn = usage.ScannedOn,
            ScannedSources = usage.ScannedSources,
            SourceHash = usage.SourceHash,
            Operations = [.. usage.Operations, new("object.find", "ObjectFindOperation", null), new("object.get", "ObjectGetOperation", null)],
            Usages = usage.Usages,
        }));
        repository.Write(PropertyDictionaryGenerator.SourcePath, repository.Read(PropertyDictionaryGenerator.SourcePath) + """
            {"object":"Arc","name":"ARC_ONE","value":"13000"}
            {"object":"Comment","name":"COMMENT_ONE","value":"13001"}
            {"object":"Function3D","name":"FUNCTION3D_ONE","value":"13002"}
            {"object":"TerminalStrip","name":"STRIP_ONE","value":"13003"}
            {"object":"ArticleReference","name":"REFERENCE_ONE","value":"13004"}

            """);
        if (size is not null)
        {
            repository.Write("tests/Host.Tests/ObjectReadTests.cs", $$"""
                [Trait("Size", "{{size}}")]
                public sealed class ObjectReaderTests { void T() { Call("eplan_object_find"); Call("eplan_object_get"); } }
                """);
        }

        return repository;
    }
}
