using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace EplanMcp.Packs.Tests;

/// <summary>The independent JSON Schema covers shape and field constraints; the reader also checks document-wide identities and byte size.</summary>
[Trait("Size", "Medium")]
public sealed class PluginLibrarySchemaTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-library-schema-");

    [Fact]
    public async Task Schema_AgreesWithReaderOnShapeValuesAndUnsafeFiles()
    {
        var cases = new List<(JsonNode Json, bool Valid)> { (Index(), true) };
        foreach (string kind in new[] { "standard", "parts", "symbols", "templates", "company" })
        {
            cases.Add((Changed("kind", JsonValue.Create(kind)), true));
        }
        cases.Add((Changed("standards", new JsonArray("IEC 60204-1:2016")), true));
        cases.Add((Changed("standards", null), false));
        cases.Add((Changed("standards", new JsonArray("IEC", "IEC")), false));
        cases.Add((Changed("standards", new JsonArray((JsonNode?)null)), false));
        cases.Add((Changed("kind", JsonValue.Create("unknown")), false));
        cases.Add((Changed("kind", JsonValue.Create(1)), false));
        cases.Add((Changed("version", JsonValue.Create("1.0")), false));
        cases.Add((Changed("version", JsonValue.Create("1.0.0\n")), false));
        cases.Add((Changed("version", JsonValue.Create("1.0.0-rc.1+build")), true));
        cases.Add((Changed("id", JsonValue.Create("base\n")), false));
        cases.Add((Changed("digest", JsonValue.Create(new string('F', 64))), false));
        cases.Add((Changed("title", JsonValue.Create("title\n")), false));
        cases.Add((Changed("extra", JsonValue.Create(true)), false));
        JsonNode missing = Index();
        missing["plugins"]![0]!.AsObject().Remove("title");
        cases.Add((missing, false));
        JsonNode otherKind = Changed("kind", JsonValue.Create("company"));
        otherKind["plugins"]![0]!["standards"] = new JsonArray("IEC");
        cases.Add((otherKind, false));
        JsonNode unknown = Index();
        unknown["extra"] = true;
        cases.Add((unknown, false));
        JsonNode nullPlugins = Index();
        nullPlugins["plugins"] = null;
        cases.Add((nullPlugins, false));
        JsonNode empty = Index();
        empty["plugins"] = new JsonArray();
        cases.Add((empty, true));
        foreach (string file in new[] { "/absolute", "C:/absolute", "C:relative", @"\\server\share\pack", "../pack", "..", ".", "a/b", @"a\b", "file:stream", "file.", "file ", "nUl.txt", "CON .txt", "Lpt1", "a\n" })
        {
            cases.Add((Changed("file", JsonValue.Create(file)), false));
        }
        cases.Add((Changed("file", JsonValue.Create("IEC 60204.eplanpack")), true));
        await CheckAsync(cases);
    }

    [Fact]
    public async Task Schema_AgreesWithReaderOnCollectionAndStringBounds()
    {
        var cases = new List<(JsonNode Json, bool Valid)>();
        foreach ((string field, int maximum) in new[] { ("title", 500), ("file", 240), ("id", 64) })
        {
            cases.Add((Changed(field, JsonValue.Create(new string('a', maximum))), true));
            cases.Add((Changed(field, JsonValue.Create(new string('a', maximum + 1))), false));
        }
        cases.Add((Changed("standards", new JsonArray(new string('a', 128))), true));
        cases.Add((Changed("standards", new JsonArray(new string('a', 129))), false));
        foreach (int count in new[] { 64, 65 })
        {
            cases.Add((Changed("standards", new JsonArray(Enumerable.Range(0, count)
                .Select(i => (JsonNode?)JsonValue.Create("s" + i.ToString(CultureInfo.InvariantCulture))).ToArray())), count == 64));
        }
        foreach (int count in new[] { 2000, 2001 })
        {
            JsonNode root = Index();
            var entries = new JsonArray();
            for (int i = 0; i < count; i++)
            {
                JsonNode entry = Index()["plugins"]![0]!.DeepClone();
                entry["id"] = "p" + i.ToString(CultureInfo.InvariantCulture);
                entry["file"] = "p" + i.ToString(CultureInfo.InvariantCulture) + ".eplanpack";
                entries.Add(entry);
            }
            root["plugins"] = entries;
            cases.Add((root, count == 2000));
        }
        await CheckAsync(cases);
    }

    private async Task CheckAsync(List<(JsonNode Json, bool Valid)> cases)
    {
        for (int i = 0; i < cases.Count; i++)
        {
            byte[] bytes = PackJson.Serialize(cases[i].Json);
            bool valid;
            try
            { _ = PluginLibraryReader.Read(bytes); valid = true; }
            catch (PackFormatException) { valid = false; }
            Assert.Equal(cases[i].Valid, valid);
            File.WriteAllBytes(Path.Combine(_directory.FullName, i.ToString("D3", CultureInfo.InvariantCulture) + ".json"), bytes);
        }
        var start = new ProcessStartInfo("pwsh", ["-NoProfile", "-NonInteractive", "-Command",
            "Get-ChildItem -LiteralPath $env:PLUGIN_CASES -Filter '*.json' | Sort-Object Name | ForEach-Object { Test-Json -LiteralPath $_.FullName -SchemaFile $env:PLUGIN_SCHEMA }"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.Environment["PLUGIN_CASES"] = _directory.FullName;
        start.Environment["PLUGIN_SCHEMA"] = Path.Combine(Repository.Root, "schemas", "plugin-library.schema.json");
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        string[] results = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(results.SequenceEqual(cases.Select(c => c.Valid ? "True" : "False")), $"stdout: {string.Join(", ", results)}; stderr: {await error}");
    }

    private static JsonNode Changed(string property, JsonNode? value)
    {
        JsonNode root = Index();
        root["plugins"]![0]![property] = value;
        return root;
    }

    private static JsonNode Index() => JsonNode.Parse("""
        {"name":"catalog","plugins":[{"id":"base","version":"1.0.0","kind":"standard","digest":"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff","file":"base.eplanpack","title":"Base","standards":[]}]}
        """)!;

    public void Dispose() => _directory.Delete(recursive: true);
}
