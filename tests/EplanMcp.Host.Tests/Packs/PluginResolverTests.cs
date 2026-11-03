using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class PluginResolverTests : IDisposable
{
    private const string SchemaPath = "knowledge/config_schema/defaults.json";
    private const string LayerPath = "knowledge/config_layer/defaults.json";
    private const string Schema = """{"items":{"key":{"type":"number","default":5,"required":false,"allowedLayers":["base","company","cabinet_type","project","cabinet"]}}}""";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-plugin-resolver-").FullName);
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;
    private PluginStateStore State => new(_locations);
    private PluginResolver Resolver => new(new PackLibrary(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, TimeProvider.System), TimeProvider.System));

    [Fact]
    public async Task WithoutState_GlobalResolutionIsEmpty()
    {
        PluginResolution result = await Resolver.ResolveAsync(Cancel);
        Assert.Empty(result.Values.EnumerateArray());
        Assert.Empty(result.Plugins);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task EnabledOrder_DeterminesWinnerAndOverriddenSources()
    {
        WritePack("plugin.first", "1.0.0", 7);
        WritePack("plugin.second", "1.0.0", 8);
        await EnableAsync(new PluginIdentity("plugin.first", "1.0.0"), new PluginIdentity("plugin.second", "1.0.0"));

        PluginResolution result = await Resolver.ResolveAsync(Cancel);

        Assert.Empty(result.Warnings);
        Assert.Equal(["plugin.first@1.0.0", "plugin.second@1.0.0"], result.Plugins);
        JsonElement item = Assert.Single(result.Values.EnumerateArray());
        Assert.Equal(8, item.GetProperty("value").GetInt32());
        Assert.Equal("plugin.second@1.0.0", item.GetProperty("winner").GetProperty("plugin").GetString());
        Assert.Equal("global", item.GetProperty("winner").GetProperty("scope").GetString());
        Assert.Equal("plugin.first@1.0.0", item.GetProperty("overridden").EnumerateArray().Last().GetProperty("plugin").GetString());
    }

    [Fact]
    public async Task StateIsReadAgain_AndOnlyTheExactInstalledVersionIsUsed()
    {
        WritePack("plugin.first", "1.0.0", 7);
        WritePack("plugin.first", "2.0.0", 8);
        PluginResolver resolver = Resolver;
        await EnableAsync(new PluginIdentity("plugin.first", "1.0.0"));
        Assert.Equal(7, Value(await resolver.ResolveAsync(Cancel)));
        await EnableAsync(new PluginIdentity("plugin.first", "2.0.0"));
        Assert.Equal(8, Value(await resolver.ResolveAsync(Cancel)));
        await EnableAsync(new PluginIdentity("plugin.first", "3.0.0"));
        PluginResolution missing = await resolver.ResolveAsync(Cancel);
        Assert.Empty(missing.Values.EnumerateArray());
        Assert.Empty(missing.Plugins);
        Assert.Contains(missing.Warnings, warning => warning.Contains("plugin.first@3.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingAndUnreadablePlugins_DoNotHideAReadablePlugin()
    {
        WritePack("plugin.bad", "1.0.0", 90);
        WritePack("plugin.good", "1.0.0", 8);
        File.WriteAllText(Path.Combine(_locations.Packs, "plugin.bad", "1.0.0", PackManifest.FileName), "{bad");
        await EnableAsync(new PluginIdentity("plugin.missing", "1.0.0"), new PluginIdentity("plugin.bad", "1.0.0"), new PluginIdentity("plugin.good", "1.0.0"));

        PluginResolution result = await Resolver.ResolveAsync(Cancel);

        Assert.Equal(8, Value(result));
        Assert.Equal(["plugin.good@1.0.0"], result.Plugins);
        Assert.Equal(2, result.Warnings.Count);
        Assert.DoesNotContain(_locations.Root, string.Join(" ", result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedConfigurationHash_IsAWarning_AndOtherPluginsRemainAvailable()
    {
        WritePack("plugin.bad", "1.0.0", 90);
        WritePack("plugin.good", "1.0.0", 8);
        File.AppendAllText(Path.Combine(_locations.Packs, "plugin.bad", "1.0.0", LayerPath), " ");
        await EnableAsync(new PluginIdentity("plugin.bad", "1.0.0"), new PluginIdentity("plugin.good", "1.0.0"));

        PluginResolution result = await Resolver.ResolveAsync(Cancel);

        Assert.Equal(8, Value(result));
        Assert.Equal(["plugin.good@1.0.0"], result.Plugins);
        Assert.Contains(result.Warnings, warning => warning.Contains("plugin.bad@1.0.0:config_layer/defaults", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "null", "{}")]
    [InlineData(true, "null", "{}")]
    [InlineData(true, "{\"name\":\"defaults\",\"tags\":null}", "{}")]
    [InlineData(true, "{\"name\":\"defaults\"}", "null")]
    [InlineData(true, "{\"name\":\"defaults\"}", "[]")]
    [InlineData(true, "{\"name\":\"defaults\"}", "42")]
    public async Task MalformedEnvelope_IsAWarningWithEitherStaleOrCorrectHash(bool updateHash, string metadata, string spec)
    {
        WritePack("plugin.bad", "1.0.0", 90);
        WritePack("plugin.good", "1.0.0", 8);
        string folder = Path.Combine(_locations.Packs, "plugin.bad", "1.0.0");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("{\"apiVersion\":\"eplanmcp.pack/v2\",\"kind\":\"config_layer\",\"metadata\":" + metadata + ",\"spec\":" + spec + "}");
        File.WriteAllBytes(Path.Combine(folder, LayerPath), bytes);
        if (updateHash)
        {
            PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(folder, PackManifest.FileName)));
            manifest = manifest with
            {
                Resources = [.. manifest.Resources.Select(resource => resource.Path == LayerPath
                    ? resource with { Bytes = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) } : resource)]
            };
            File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), PackJson.Serialize(manifest));
        }
        await EnableAsync(new PluginIdentity("plugin.bad", "1.0.0"), new PluginIdentity("plugin.good", "1.0.0"));

        PluginResolution result = await Resolver.ResolveAsync(Cancel);

        Assert.Equal(8, Value(result));
        Assert.Equal(["plugin.good@1.0.0"], result.Plugins);
        Assert.Contains(result.Warnings, warning => warning.Contains("plugin.bad@1.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnreferencedConfigurationResources_AreNotApplied()
    {
        WritePack("plugin.first", "1.0.0", 7, unreferencedLayer: true);
        await EnableAsync(new PluginIdentity("plugin.first", "1.0.0"));
        PluginResolution result = await Resolver.ResolveAsync(Cancel);
        Assert.Equal(7, Value(result));
        Assert.Empty(result.Warnings);
        Assert.DoesNotContain("unreferenced", result.Values.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BindingChain_RetainsPrecedenceAndCompleteTrace()
    {
        WritePack("plugin.first", "1.0.0", 7);
        await EnableAsync(new PluginIdentity("plugin.first", "1.0.0"));
        ProjectBinding binding = Binding() with { ProjectValues = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>("""{"key":{"default":9}}""") };
        ConfigDocument schema = Document("bound@1.0.0:config_schema/defaults", Schema);
        ConfigDocument layer = Document("bound@1.0.0:config_layer/defaults", """{"layer":"company","schema":"config_schema/defaults","settings":{"key":{"default":8}}}""");

        PluginResolution result = await Resolver.ResolveAsync([schema], [layer], binding, 0, Cancel);

        Assert.Empty(result.Warnings);
        Assert.Equal(9, Value(result));
        Assert.Equal("project", result.Values[0].GetProperty("winner").GetProperty("layer").GetString());
        Assert.Contains(result.Values[0].GetProperty("overridden").EnumerateArray(), item => item.TryGetProperty("plugin", out JsonElement plugin) && plugin.GetString() == "plugin.first@1.0.0");
    }

    [Fact]
    public async Task BoundId_SkipsGlobalPluginWithoutReadingItsMissingFiles()
    {
        await EnableAsync(new PluginIdentity("plugin.first", "1.0.0"));
        ProjectBinding binding = Binding() with { Locks = [new() { Id = "plugin.first", Version = "2.0.0", Digest = new string('a', 64) }] };
        PluginResolution result = await Resolver.ResolveAsync([], [], binding, 0, Cancel);
        Assert.Empty(result.Values.EnumerateArray());
        Assert.Empty(result.Plugins);
        Assert.Contains("skipped", Assert.Single(result.Warnings), StringComparison.Ordinal);
        JsonElement warning = Assert.Single(result.Trace.EnumerateArray());
        Assert.Equal("global", warning.GetProperty("scope").GetString());
        Assert.Equal("plugin.first@1.0.0", warning.GetProperty("plugin").GetString());
    }

    [Fact]
    public async Task UnreadableState_StillResolvesTheSuppliedBindingChain()
    {
        Directory.CreateDirectory(_locations.State);
        File.WriteAllText(Path.Combine(_locations.State, "plugins.json"), "{bad");
        ConfigDocument schema = Document("bound@1.0.0:config_schema/defaults", Schema);
        PluginResolution result = await Resolver.ResolveAsync([schema], [], Binding(), 0, Cancel);
        Assert.Equal(5, Value(result));
        Assert.Single(result.Warnings);
        Assert.Empty(result.Plugins);
    }

    [Fact]
    public async Task Cancellation_IsNotConvertedToADataWarning()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Resolver.ResolveAsync(cancelled.Token));
    }

    private async Task EnableAsync(params PluginIdentity[] enabled)
    {
        (_, string? revision) = await State.ReadAsync(Cancel);
        await State.WriteAsync(new(1, enabled, []), revision, Cancel);
    }

    private void WritePack(string id, string version, int value, bool unreferencedLayer = false)
    {
        string folder = Path.Combine(_locations.Packs, id, version);
        var resources = new List<PackResource>();
        Add(SchemaPath, ResourceKind.ConfigSchema, Schema);
        Add(LayerPath, ResourceKind.ConfigLayer, Layer(value));
        if (unreferencedLayer)
        { Add("knowledge/config_layer/unreferenced.json", ResourceKind.ConfigLayer, Layer(99)); }
        var manifest = new PackManifest
        {
            SchemaVersion = 3,
            Id = id,
            Name = id,
            Version = version,
            Compatibility = new(),
            Resources = [.. resources.OrderBy(item => item.Path, StringComparer.Ordinal)],
            Configuration = new() { Company = new() { Id = "defaults", Resources = [LayerPath] } },
            Provenance = new() { CreatedAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), CreatedBy = "eplanmcp test", Inputs = [] }
        };
        byte[] bytes = PackJson.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), bytes);
        var pin = new PackReference { Id = id, Version = version, Digest = PackDigest.Compute(bytes) };
        File.WriteAllBytes(Path.Combine(folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(pin, [])));

        void Add(string path, ResourceKind kind, string spec)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            byte[] content = PackJson.Serialize(new { apiVersion = "eplanmcp.pack/v2", kind = kind == ResourceKind.ConfigSchema ? "config_schema" : "config_layer", metadata = new { name }, spec = JsonSerializer.Deserialize<JsonElement>(spec) });
            string target = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, content);
            resources.Add(new() { Path = path, Kind = kind, Storage = ResourceStorage.Bundled, Bytes = content.Length, Files = 1, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
        }
    }

    private static string Layer(int value) => JsonSerializer.Serialize(new { layer = "company", schema = "config_schema/defaults", settings = new { key = new { @default = value } } });
    private static int Value(PluginResolution result) => Assert.Single(result.Values.EnumerateArray()).GetProperty("value").GetInt32();
    private static ConfigDocument Document(string id, string spec) => new(id, new string('a', 64), JsonSerializer.Deserialize<JsonElement>(spec));
    private static ProjectBinding Binding() => new()
    {
        BindingVersion = 2,
        Project = "test",
        Revision = 0,
        PackRoots = [],
        ProjectValues = new(),
        Cabinets = [new() { Id = "default", Layers = [], Values = new(), Structure = new() { ["all"] = true } }]
    };

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);
}
