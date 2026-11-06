using System.Text.Json;
using EplanMcp.Packs.Knowledge;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class BuiltinPluginContentTests
{
    private static string Source => Path.Combine(Repository.Root, "packaging", "plugin-libraries", "builtin");
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GeneratedArchive_PassesVerifierAndContainsOnlyReviewedDefaults()
    {
        using var temporary = new TempFolder();
        string output = Path.Combine(temporary.Root, "library");
        await PluginLibraryBuilder.BuildAsync(Source, output, Cancel);
        PluginLibraryIndex index = PluginLibraryReader.Read(await File.ReadAllBytesAsync(Path.Combine(output, "library.json"), Cancel));
        PluginLibraryEntry entry = Assert.Single(index.Plugins, plugin => plugin.Id == "iec-base");
        Assert.Equal("builtin", index.Name);
        Assert.Equal("iec-base", entry.Id);
        Assert.Equal(PackKind.Standard, entry.Kind);
        Assert.Equal(["IEC 60617", "IEC 81346-2"], entry.Standards);
        string extracted = Path.Combine(temporary.Root, "extracted");
        PackCheckResult check = await PackArchive.ExtractAsync(Path.Combine(output, entry.File), extracted, PackLimits.Default, Cancel);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        Assert.Equal(entry.Digest, PackDigest.Compute(await File.ReadAllBytesAsync(Path.Combine(extracted, "pack.json"), Cancel)));
        PackManifest manifest = Assert.IsType<PackManifest>(check.Manifest);
        Assert.Equal(["defaults.strip_definition.symbol", "defaults.strip_definition.variant"], manifest.Provides);
        Assert.Empty(manifest.Rules);

        JsonElement schema = await SpecAsync(extracted, "config_schema", "defaults");
        Assert.Equal(3, schema.GetProperty("items").EnumerateObject().Count());
        Assert.Equal("text", schema.GetProperty("items").GetProperty("defaults.interruption.symbol").GetProperty("type").GetString());
        JsonElement settings = (await SpecAsync(extracted, "config_layer", "defaults")).GetProperty("settings");
        Assert.Equal(3, settings.EnumerateObject().Count());
        Assert.Equal("TDEF", settings.GetProperty("defaults.strip_definition.symbol").GetProperty("default").GetString());
        Assert.Equal(0, settings.GetProperty("defaults.strip_definition.variant").GetProperty("default").GetInt32());
        Assert.Equal("BP", settings.GetProperty("defaults.interruption.symbol").GetProperty("default").GetString());

        JsonElement facts = await SpecAsync(extracted, "fact_set", "symbols");
        Assert.Equal("api-notes A83", facts.GetProperty("evidence").GetString());
        Assert.Equal(["BP", "BPIN", "BPOUT", "BPF", "TDEF", "PLCC", "PLCCP", "PLCCPOV", "PLCCPP"],
            facts.GetProperty("symbols").EnumerateArray().Select(symbol => symbol.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task RebuildingSameSource_ProducesIdenticalDigestAndArchive()
    {
        using var temporary = new TempFolder();
        string output = Path.Combine(temporary.Root, "library");
        await PluginLibraryBuilder.BuildAsync(Source, output, Cancel);
        byte[] firstIndex = await File.ReadAllBytesAsync(Path.Combine(output, "library.json"), Cancel);
        PluginLibraryEntry entry = Assert.Single(PluginLibraryReader.Read(firstIndex).Plugins, plugin => plugin.Id == "iec-base");
        byte[] firstArchive = await File.ReadAllBytesAsync(Path.Combine(output, entry.File), Cancel);
        await PluginLibraryBuilder.BuildAsync(Source, output, Cancel);
        Assert.Equal(firstIndex, await File.ReadAllBytesAsync(Path.Combine(output, "library.json"), Cancel));
        Assert.Equal(firstArchive, await File.ReadAllBytesAsync(Path.Combine(output, entry.File), Cancel));
    }

    private static async Task<JsonElement> SpecAsync(string root, string kind, string name)
    {
        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(root, "knowledge", kind, name + ".json"), Cancel);
        return JsonSerializer.SerializeToElement(KnowledgeDocument.Read(bytes).Spec, PackJson.Options);
    }
}
