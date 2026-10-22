namespace EplanMcp.Catalog.Tests;

/// <summary>The extractor over a fake SDK folder: this test assembly copied under an EPLAN API file name.</summary>
[Trait("Size", "Medium")]
public sealed class ExtractorTests : IDisposable
{
    private readonly DirectoryInfo _sdk = Directory.CreateTempSubdirectory("eplanmcp-catalog-sdk-");

    public ExtractorTests()
    {
        string assembly = typeof(ExtractorTests).Assembly.Location;
        File.Copy(assembly, Path.Combine(_sdk.FullName, "Eplan.EplApi.Fixtureu.dll"));
        File.Copy(Path.ChangeExtension(assembly, ".xml"), Path.Combine(_sdk.FullName, "Eplan.EplApi.Fixtureu.xml"));
        File.Copy(assembly, Path.Combine(_sdk.FullName, "Eplan.EplApi.NoDocsu.dll"));
    }

    public void Dispose() => _sdk.Delete(recursive: true);

    [Fact]
    public void Extract_ReadsAssembliesWithAnIntelliSenseFileAndSkipsTheRest()
    {
        CatalogData catalog = Extractor.Extract(_sdk.FullName, AssemblyScannerTests.Prefix);

        Assert.Equal(["Eplan.EplApi.Fixtureu.dll", "Eplan.EplApi.NoDocsu.dll"], catalog.Assemblies.Select(a => a.File));
        Assert.Equal((null, "EplanMcp.Catalog.Tests"), (catalog.Assemblies[0].Skipped, catalog.Assemblies[0].Name));
        Assert.Equal("no_intellisense_file", catalog.Assemblies[1].Skipped);
        Assert.Empty(catalog.Xml.Unmatched);
        Assert.Equal(0, catalog.Duplicates);
        Assert.Equal(catalog.Entries.Select(e => e.Id).Order(StringComparer.Ordinal), catalog.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Extract_MarksDocumentationAndClassifiesEveryEntry()
    {
        CatalogData catalog = Extractor.Extract(_sdk.FullName, AssemblyScannerTests.Prefix);
        CatalogEntry Get(string id) => catalog.Entries.Single(e => e.Id == id);

        Assert.All(catalog.Entries, e => Assert.NotEqual("", e.Layer));
        Assert.True(Get("M:EplanMcp.Catalog.Tests.Fixture.Widget.Move(System.Double@,System.Double@)").Documented);
        Assert.False(Get("M:EplanMcp.Catalog.Tests.Fixture.ShapeHandler.Invoke(EplanMcp.Catalog.Tests.Fixture.IShape)").Documented);
        Assert.Equal((Classifier.DataDictionary, "Widget"),
            (Get("P:EplanMcp.Catalog.Tests.Fixture.WidgetPropertyList.WIDGET_NAME").Layer, Get("P:EplanMcp.Catalog.Tests.Fixture.WidgetPropertyList.WIDGET_NAME").Object));
        Assert.Equal((Classifier.DataDictionary, "Widget"),
            (Get("F:EplanMcp.Catalog.Tests.Fixture.Properties.Widget.WIDGET_NAME").Layer, Get("F:EplanMcp.Catalog.Tests.Fixture.Properties.Widget.WIDGET_NAME").Object));
        Assert.Equal(Classifier.Operation, Get("P:EplanMcp.Catalog.Tests.Fixture.WidgetPropertyList.Property").Layer);
        Assert.Equal(new Exclusion("deprecated", "obsolete_attribute"), Get("M:EplanMcp.Catalog.Tests.Fixture.Legacy.Run").Excluded);
    }

    [Fact]
    public void Extract_CountsIntelliSenseIdsOutsideThePrefixAndOfNonPublicMembers()
    {
        XmlStats xml = Extractor.Extract(_sdk.FullName, AssemblyScannerTests.Prefix).Xml;

        Assert.True(xml.NonPublic >= 5, $"non-public: {xml.NonPublic}");
        Assert.True(xml.OutsideNamespace > 0, "the test classes' own doc comments are outside the prefix");
        Assert.Equal(xml.Members, xml.Public + xml.NonPublic + xml.OutsideNamespace + xml.Unmatched.Count);
    }
}
