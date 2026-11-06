using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Host.Tests.Packs;

public sealed partial class PluginResolverTests
{
    private static readonly string[] _pluginInclude = ["plugins"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PluginContext_CountsV2RulesAndReportsEffectiveSignoff(bool isSigned)
    {
        WritePack("plugin.rules", "1.0.0", 7);
        string folder = Path.Combine(_locations.Packs, "plugin.rules", "1.0.0");
        string manifestPath = Path.Combine(folder, PackManifest.FileName);
        PackManifest manifest = PackJson.ReadManifest(await File.ReadAllBytesAsync(manifestPath, Cancel));
        var resources = manifest.Resources.ToList();
        foreach ((KnowledgeKind kind, string version) in new[] { (KnowledgeKind.Rule, KnowledgeDocument.V2ApiVersion), (KnowledgeKind.Candidate, KnowledgeDocument.CurrentApiVersion) })
        {
            var document = new KnowledgeDocument
            {
                ApiVersion = version,
                Kind = kind,
                Metadata = new() { Name = "example" },
                Spec = JsonNode.Parse("""{"severity":"mandatory","source":{"standard":"test","clause":"1"},"scope":{"kind":"function","where":{"literal":true}},"check":{"literal":true},"message":{"en":"Review example"}}""")!.AsObject()
            };
            string path = KnowledgeDocument.PathOf(kind, document.Metadata.Name);
            byte[] content = PackJson.Serialize(document);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, path))!);
            await File.WriteAllBytesAsync(Path.Combine(folder, path), content, Cancel);
            resources.Add(new() { Path = path, Kind = ResourceKind.Rule, Storage = ResourceStorage.Bundled, Bytes = content.Length, Files = 1, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
        }
        await File.WriteAllBytesAsync(manifestPath, PackJson.Serialize(manifest with
        {
            Resources = [.. resources.OrderBy(resource => resource.Path, StringComparer.Ordinal)],
            Signoff = isSigned ? new() { Status = "signed", SignedBy = "Engineer", SignedAt = "2026-11-06" } : null
        }), Cancel);
        await EnableAsync(new PluginIdentity("plugin.rules", "1.0.0"));
        var packs = new PackLibrary(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, TimeProvider.System), TimeProvider.System);

        JsonObject result = await PluginContextProjection.OfflineAsync(packs, JsonSerializer.SerializeToElement(new { }), 5000, Cancel);

        JsonNode plugin = Assert.Single(result["plugins"]!.AsArray())!;
        Assert.Equal(1, plugin["ruleCount"]!.GetValue<int>());
        Assert.Equal(isSigned ? "signed" : "draft", plugin["signoff"]!["status"]!.GetValue<string>());
        if (isSigned)
        {
            Assert.Equal("Engineer", plugin["signoff"]!["signedBy"]!.GetValue<string>());
            Assert.Equal("2026-11-06", plugin["signoff"]!["signedAt"]!.GetValue<string>());
        }
    }
    [Fact]
    public async Task PluginContext_OfflineHasNoBridgeCallsAndReportsMissingData()
    {
        WritePack("plugin.first", "1.0.0", 7);
        await EnableAsync(new PluginIdentity("plugin.first", "1.0.0"), new PluginIdentity("missing", "1.0.0"));
        var bridge = new FakeBridgeClient();
        var packs = new PackLibrary(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, TimeProvider.System), TimeProvider.System);
        JsonElement result = ContextToolTests.Data(await ContextTools.GetAsync(bridge, packs, JsonSerializer.SerializeToElement(new { }),
            include: JsonSerializer.SerializeToElement(_pluginInclude), cancellationToken: Cancel));
        Assert.Empty(bridge.Calls);
        Assert.Null(result.GetProperty("project").GetString());
        Assert.Equal(2, result.GetProperty("plugins").GetArrayLength());
        Assert.All(result.GetProperty("plugins").EnumerateArray(), p => Assert.Equal("global", p.GetProperty("scope").GetString()));
        Assert.NotEmpty(result.GetProperty("coverage").GetProperty("missing").EnumerateArray());
        Assert.Null(result.GetProperty("contextRef").GetProperty("snapshotHash").GetString());
        ContextToolTests.AssertSources(result);
    }

    [Fact]
    public async Task PluginContext_EffectiveDefaultsAndOverriddenSourcesRespectBudget()
    {
        WritePack("plugin.first", "1.0.0", 7, key: "defaults.test");
        WritePack("plugin.second", "1.0.0", 8, key: "defaults.test");
        foreach (string id in new[] { "plugin.first", "plugin.second" })
        {
            string path = Path.Combine(_locations.Packs, id, "1.0.0", PackManifest.FileName);
            PackManifest manifest = PackJson.ReadManifest(await File.ReadAllBytesAsync(path, Cancel));
            await File.WriteAllBytesAsync(path, PackJson.Serialize(manifest with { Provides = ["defaults.test"], Kind = PackKind.Standard, Standards = ["IEC test"] }), Cancel);
        }
        await EnableAsync(new PluginIdentity("plugin.first", "1.0.0"), new PluginIdentity("plugin.second", "1.0.0"));
        var packs = new PackLibrary(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, TimeProvider.System), TimeProvider.System);
        var all = await PluginContextProjection.OfflineAsync(packs, JsonSerializer.SerializeToElement(new { }), 20000, Cancel);
        Assert.Equal("standard", all["plugins"]![0]!["kind"]!.GetValue<string>());
        Assert.Equal(0, all["plugins"]![0]!["ruleCount"]!.GetValue<int>());
        Assert.Equal(8, all["plugins"]![0]!["defaults"]![0]!["value"]!.GetValue<int>());
        Assert.Equal("plugin.second@1.0.0", all["plugins"]![0]!["defaults"]![0]!["winner"]!["plugin"]!.GetValue<string>());
        Assert.Contains("plugin.first@1.0.0", all["plugins"]![0]!["defaults"]![0]!["overridden"]!.ToJsonString(), StringComparison.Ordinal);
        int tokens = all["budget"]!["estimatedTokens"]!.GetValue<int>();
        var limited = await PluginContextProjection.OfflineAsync(packs, JsonSerializer.SerializeToElement(new { }), tokens - 30, Cancel);
        Assert.True(limited["truncated"]!.GetValue<bool>());
        Assert.True(limited["budget"]!["omittedItems"]!.GetValue<int>() > 0);
        Assert.True(limited["budget"]!["estimatedTokens"]!.GetValue<int>() <= tokens - 30);
    }
}
