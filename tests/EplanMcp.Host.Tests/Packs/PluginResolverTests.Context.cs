using System.Text.Json;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

public sealed partial class PluginResolverTests
{
    private static readonly string[] _pluginInclude = ["plugins"];
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
