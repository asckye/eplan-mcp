using System.Text.Json;

namespace EplanMcp.Catalog.Tests;

/// <summary>The written catalog: deterministic, shaped as catalog/schema/catalog.schema.json says, enum values and
/// property IDs folded as the design describes. Writes to temporary folders, hence Medium.</summary>
[Trait("Size", "Medium")]
public sealed class CatalogWriterTests : IDisposable
{
    private const string Fixture = "EplanMcp.Catalog.Tests.Fixture";
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-catalog-out-");
    private readonly string _sdk;

    public CatalogWriterTests()
    {
        _sdk = Directory.CreateDirectory(Path.Combine(_root.FullName, "sdk")).FullName;
        string assembly = typeof(CatalogWriterTests).Assembly.Location;
        File.Copy(assembly, Path.Combine(_sdk, "Eplan.EplApi.Fixtureu.dll"));
        File.Copy(Path.ChangeExtension(assembly, ".xml"), Path.Combine(_sdk, "Eplan.EplApi.Fixtureu.xml"));
    }

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void Write_TwiceFromTheSameSdk_GivesIdenticalFiles()
    {
        string first = Write("first");
        string second = Write("second");

        string[] files = [.. Directory.GetFiles(first, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(first, f)).Order(StringComparer.Ordinal)];
        Assert.Equal(["assemblies.json", "data-dictionary.jsonl", Path.Combine("members", Fixture + ".jsonl"), "summary.json", "types.jsonl"], files);
        Assert.All(files, f => Assert.Equal(File.ReadAllBytes(Path.Combine(first, f)), File.ReadAllBytes(Path.Combine(second, f))));
        Assert.DoesNotContain(_sdk, File.ReadAllText(Path.Combine(first, "assemblies.json")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_EveryLine_UsesOnlyTheSchemasFields()
    {
        string output = Write("out");
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "catalog", "schema", "catalog.schema.json")));
        JsonElement defs = schema.RootElement.GetProperty("$defs");

        string[] entryLines = [.. File.ReadAllLines(Path.Combine(output, "types.jsonl")), .. File.ReadAllLines(Path.Combine(output, "members", Fixture + ".jsonl"))];
        Assert.All(entryLines, line => AssertFields(defs.GetProperty("entry"), line));
        Assert.All(File.ReadAllLines(Path.Combine(output, "data-dictionary.jsonl")), line => AssertFields(defs.GetProperty("dictionaryProperty"), line));
    }

    [Fact]
    public void Write_FoldsEnumValuesIntoTheirTypeAndPropertyIdsIntoOneLine()
    {
        string output = Write("out");

        string shade = File.ReadAllLines(Path.Combine(output, "types.jsonl")).Single(l => l.StartsWith($"{{\"id\":\"T:{Fixture}.Shade\"", StringComparison.Ordinal));
        Assert.Contains("\"values\":{\"Dark\":\"2\",\"Light\":\"1\"}", shade, StringComparison.Ordinal);
        Assert.Equal(["{\"object\":\"Widget\",\"name\":\"WIDGET_NAME\",\"value\":\"20001\",\"indexed\":false}"],
            File.ReadAllLines(Path.Combine(output, "data-dictionary.jsonl")));
        Assert.DoesNotContain(File.ReadAllLines(Path.Combine(output, "members", Fixture + ".jsonl")), l => l.Contains("WIDGET_NAME", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_Summary_CountsOperationGroupsAndTheDataDictionary()
    {
        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(Write("out"), "summary.json")));
        JsonElement layers = summary.RootElement.GetProperty("layers");
        JsonElement widget = layers.GetProperty("data_dictionary").GetProperty("byObject").GetProperty("Widget");

        Assert.Equal(0, summary.RootElement.GetProperty("intellisense").GetProperty("unmatched").GetInt32());
        Assert.True(layers.GetProperty("operation").GetProperty("denominatorGroups").GetInt32() > 10);
        Assert.Equal($"T:{Fixture}.WidgetPropertyList", widget.GetProperty("propertyList").GetString());
        Assert.Equal($"T:{Fixture}.Properties.Widget", widget.GetProperty("enum").GetString());
        Assert.Equal(1, widget.GetProperty("properties").GetInt32());
    }

    [Theory]
    [InlineData("sdk")]
    [InlineData("sdk/catalog")]
    public void Main_OutputInTheSdkFolder_IsRefused(string output)
    {
        int exit = Program.Main(["extract", "--sdk", _sdk, "--out", Path.Combine(_root.FullName, output)]);

        Assert.Equal(2, exit);
        Assert.False(Directory.Exists(Path.Combine(_sdk, "catalog")));
    }

    [Fact]
    public void Main_WithoutArguments_PrintsUsage() => Assert.Equal(2, Program.Main([]));

    private string Write(string name)
    {
        string output = Path.Combine(_root.FullName, name);
        Assert.Equal(0, Program.Main(["extract", "--sdk", _sdk, "--out", output, "--namespace", Fixture]));
        return output;
    }

    private static void AssertFields(JsonElement definition, string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement properties = definition.GetProperty("properties");
        Assert.All(document.RootElement.EnumerateObject(), p => Assert.True(properties.TryGetProperty(p.Name, out _), $"'{p.Name}' is not in the schema: {line}"));
        Assert.All(definition.GetProperty("required").EnumerateArray(), r => Assert.True(document.RootElement.TryGetProperty(r.GetString()!, out _), line));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("EplanMcp.slnx not found above " + AppContext.BaseDirectory);
    }
}
