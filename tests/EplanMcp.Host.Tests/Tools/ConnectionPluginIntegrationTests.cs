using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ConnectionPluginIntegrationTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealResolution_UsesPluginOnceAndReportsOnlyConsumedSource(bool dryRun)
    {
        using var harness = Setup();
        WritePack(harness, "1.0.0", "BPIN");
        await EnableAsync(harness, "1.0.0");
        JsonElement result = WriteToolHarness.Structured(await CallAsync(harness, dryRun));
        Assert.Equal("BPIN", result.GetProperty("symbol").GetString());
        Assert.Equal("standard@1.0.0", Assert.Single(result.GetProperty("metadata").GetProperty("plugins").EnumerateArray()).GetString());
        JsonElement planned = JsonSerializer.SerializeToElement(harness.Bridge.Calls.Single(call => call.Operation == OperationNames.ConnectionCreatePlan).Args);
        Assert.Equal("BPIN", planned.GetProperty("items")[0].GetProperty("symbol").GetString());
        Assert.Equal(dryRun ? 0 : 1, harness.Bridge.Writes.Count);
        if (!dryRun)
        {
            JsonElement executed = JsonSerializer.SerializeToElement(harness.Bridge.Calls.Single(call => call.Operation == OperationNames.ConnectionCreateExecute).Args).GetProperty("request");
            Assert.Equal(planned.GetRawText(), executed.GetRawText());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitOrAbsentPlugin_DoesNotReportPlugin(bool explicitSymbol)
    {
        using var harness = Setup();
        if (explicitSymbol)
        {
            WritePack(harness, "1.0.0", "BPIN");
            await EnableAsync(harness, "1.0.0");
        }
        JsonElement result = WriteToolHarness.Structured(await CallAsync(harness, true, explicitSymbol ? "BPOUT" : null));
        Assert.Equal(explicitSymbol ? "BPOUT" : "BPF", result.GetProperty("symbol").GetString());
        Assert.False(result.TryGetProperty("metadata", out _));
    }

    [Fact]
    public async Task EnabledVersionChangesAfterPlan_IsStaleBeforeWriteEvenWithSameValue()
    {
        using var harness = Setup();
        WritePack(harness, "1.0.0", "BPIN");
        WritePack(harness, "2.0.0", "BPIN");
        await EnableAsync(harness, "1.0.0");
        Func<object?, object> plan = harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan];
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = args =>
        {
            File.WriteAllBytes(Path.Combine(harness.Locations.State, "plugins.json"), PackJson.Serialize(new PluginState(1, [new("standard", "2.0.0")], [])));
            return plan(args);
        };
        JsonElement error = WriteToolHarness.Error(await CallAsync(harness, false));
        Assert.Equal(ErrorReason.PlanStale, error.GetProperty("reason").GetString());
        Assert.Equal(ExecutionState.NotStarted, error.GetProperty("execution").GetString());
        Assert.Empty(harness.Bridge.Writes);
    }

    [Fact]
    public async Task UnreadableStateAfterPlan_WarnsAndPreservesResolvedValue()
    {
        using var harness = Setup();
        WritePack(harness, "1.0.0", "BPIN");
        await EnableAsync(harness, "1.0.0");
        Func<object?, object> plan = harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan];
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = args =>
        {
            File.WriteAllText(Path.Combine(harness.Locations.State, "plugins.json"), "{bad");
            return plan(args);
        };
        JsonElement result = WriteToolHarness.Structured(await CallAsync(harness, false));
        Assert.Equal("BPIN", result.GetProperty("symbol").GetString());
        Assert.Contains("unreadable", Assert.Single(result.GetProperty("metadata").GetProperty("warnings").EnumerateArray()).GetString(), StringComparison.Ordinal);
        Assert.Single(harness.Bridge.Writes);
    }

    [Fact]
    public async Task MissingEnabledPlugin_WarnsWithoutClaimingAPluginWasUsed()
    {
        using var harness = Setup();
        await EnableAsync(harness, "9.0.0");
        JsonElement result = WriteToolHarness.Structured(await CallAsync(harness, true));
        Assert.Equal("BPF", result.GetProperty("symbol").GetString());
        Assert.False(result.GetProperty("metadata").TryGetProperty("plugins", out _));
        Assert.NotEmpty(result.GetProperty("metadata").GetProperty("warnings").EnumerateArray());
    }

    private static WriteToolHarness Setup()
    {
        var harness = new WriteToolHarness();
        harness.Bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Name = "Line1", Path = harness.ProjectPath } };
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = args =>
        {
            JsonObject request = JsonSerializer.SerializeToNode(args)!.AsObject();
            string symbol = request["items"]![0]!["symbol"]?.GetValue<string>() ?? "BPF";
            WritePlan plan = harness.Plan([new() { Action = "create", Kind = "connections", Name = "page" }]);
            plan.Details = new() { ["preview"] = new JsonObject { ["project"] = "Line1", ["page"] = "page", ["undoSteps"] = 1, ["symbol"] = symbol }.ToJsonString() };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = args =>
        {
            JsonObject request = JsonSerializer.SerializeToNode(args)!["request"]!.AsObject();
            return new JsonObject { ["project"] = "Line1", ["page"] = "page", ["undoSteps"] = 1, ["symbol"] = request["items"]![0]!["symbol"]?.DeepClone() ?? JsonValue.Create("BPF") };
        };
        return harness;
    }

    private static Task<ModelContextProtocol.Protocol.CallToolResult> CallAsync(WriteToolHarness harness, bool dryRun, string? symbol = null)
    {
        var item = new JsonObject { ["kind"] = "interruption_point" };
        if (symbol is not null)
        { item["symbol"] = symbol; }
        var packs = new PackLibrary(harness.Locations, HostConfig.Defaults(harness.Locations), new AuditLog(harness.Locations.Audit, TimeProvider.System), TimeProvider.System);
        return ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [JsonSerializer.SerializeToElement(item)], dry_run: dryRun, cancellationToken: Cancel, packs: packs);
    }

    private static async Task EnableAsync(WriteToolHarness harness, string version)
    {
        var store = new PluginStateStore(harness.Locations);
        (_, string? revision) = await store.ReadAsync(Cancel);
        await store.WriteAsync(new(1, [new("standard", version)], []), revision, Cancel);
    }

    private static void WritePack(WriteToolHarness harness, string version, string symbol)
    {
        const string schemaPath = "knowledge/config_schema/defaults.json", layerPath = "knowledge/config_layer/defaults.json";
        string folder = Path.Combine(harness.Locations.Packs, "standard", version);
        var resources = new List<PackResource>();
        Add(schemaPath, ResourceKind.ConfigSchema, """{"items":{"defaults.interruption.symbol":{"type":"text","required":false,"allowedLayers":["base","company","cabinet_type","project","cabinet"]}}}""");
        Add(layerPath, ResourceKind.ConfigLayer, JsonSerializer.Serialize(new { layer = "company", schema = "config_schema/defaults", settings = new Dictionary<string, object> { ["defaults.interruption.symbol"] = new { @default = symbol } } }));
        var manifest = new PackManifest
        {
            SchemaVersion = 3,
            Id = "standard",
            Name = "Standard",
            Version = version,
            Kind = PackKind.Standard,
            Compatibility = new(),
            Resources = [.. resources.OrderBy(item => item.Path, StringComparer.Ordinal)],
            Configuration = new() { Company = new() { Id = "defaults", Resources = [layerPath] } },
            Provenance = new() { CreatedAt = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), CreatedBy = "eplanmcp test", Inputs = [] },
        };
        byte[] bytes = PackJson.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), bytes);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(new() { Id = "standard", Version = version, Digest = PackDigest.Compute(bytes) }, [])));
        void Add(string path, ResourceKind kind, string spec)
        {
            byte[] content = PackJson.Serialize(new { apiVersion = "eplanmcp.pack/v2", kind = kind == ResourceKind.ConfigSchema ? "config_schema" : "config_layer", metadata = new { name = "defaults" }, spec = JsonSerializer.Deserialize<JsonElement>(spec) });
            string target = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, content);
            resources.Add(new() { Path = path, Kind = kind, Storage = ResourceStorage.Bundled, Bytes = content.Length, Files = 1, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
        }
    }
}
