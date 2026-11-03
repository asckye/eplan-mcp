using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSymbolPlanTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PluginStripDefinition_UsesAndReportsExactEffectiveValues(bool dryRun)
    {
        TerminalSymbol();
        PackLibrary packs = await StripPluginAsync(_harness);
        JsonElement result = WriteToolHarness.Structured(await Terminal(dryRun: dryRun, text: "Fictional box", position: [72, 120], packs: packs));
        Assert.Equal("test.strip@1.0.0", Assert.Single(result.GetProperty("metadata").GetProperty("plugins").EnumerateArray()).GetString());
        DevicePlaceArgs planned = Assert.IsType<DevicePlaceArgs>(_harness.Bridge.Calls.First(call => call.Operation == OperationNames.DevicePlacePlan).Args);
        Assert.Equal(("SPECIAL", "TEST_DEF", 3), (planned.StripDefinition!.Library, planned.StripDefinition.Symbol, planned.StripDefinition.Variant));
        Assert.Contains("SPECIAL/TEST_DEF/3", result.GetProperty("changes")[0].GetProperty("newValue").GetString(), StringComparison.Ordinal);
        if (!dryRun)
        { Assert.Same(planned.StripDefinition, _placement.Args!.StripDefinition); }
    }

    [Fact]
    public async Task PluginStripDefinition_UnplacedAndExistingStripsDoNotConsumeDefaults()
    {
        TerminalSymbol();
        PackLibrary packs = await StripPluginAsync(_harness);
        JsonElement unplaced = WriteToolHarness.Structured(await Terminal(dryRun: false, packs: packs));
        Assert.False(unplaced.TryGetProperty("metadata", out _));
        Assert.Null(_placement.Args!.StripDefinition);
        JsonElement existing = WriteToolHarness.Structured(await Terminal(number: "2", packs: packs));
        Assert.False(existing.TryGetProperty("metadata", out _));
    }

    [Fact]
    public async Task WithoutPlugin_PlacedDefinitionKeepsBuiltinDefaultAndNoMetadata()
    {
        TerminalSymbol();
        PackLibrary packs = new(_harness.Locations, HostConfig.Defaults(_harness.Locations), new AuditLog(_harness.Locations.Audit, TimeProvider.System), TimeProvider.System);
        _harness.Bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = _store.Project };
        JsonElement result = WriteToolHarness.Structured(await Terminal(text: "Fictional box", position: [72, 120], packs: packs));
        Assert.False(result.TryGetProperty("metadata", out _));
        DevicePlaceArgs planned = Assert.IsType<DevicePlaceArgs>(_harness.Bridge.Calls.First(call => call.Operation == OperationNames.DevicePlacePlan).Args);
        Assert.Null(planned.StripDefinition);
    }

    [Fact]
    public async Task EnabledPluginChangeDuringApproval_MakesPlanStaleWithoutExecuting()
    {
        TerminalSymbol();
        string pluginStateFile = string.Empty;
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Granted)
        {
            Wait = () => File.WriteAllText(pluginStateFile, """{"version":1,"enabled":[],"libraries":[]}""")
        };
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        pluginStateFile = Path.Combine(harness.Locations.State, "plugins.json");
        Bind(harness);
        PackLibrary packs = await StripPluginAsync(harness);
        JsonElement error = WriteToolHarness.Error(await Terminal(dryRun: false, text: "Fictional box", position: [72, 120], harness: harness, packs: packs));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_placement.Calls);
        Assert.Empty(harness.Bridge.Writes);
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("variant")]
    [InlineData("plugin")]
    public void StripDefinitionChange_CannotReusePlanFingerprint(string field)
    {
        TerminalSymbol();
        var args = new DevicePlaceArgs { Project = "Line1", Page = Page, TerminalStrip = "-X6", TerminalNumber = "1", TerminalStripText = "Box", TerminalStripPosition = [72, 120], X = 80, Y = 160, StripDefinition = new() { Plugins = ["test.strip@1.0.0"] } };
        args.Fingerprint = Plan(args).Fingerprint;
        switch (field)
        {
            case "symbol":
                args.StripDefinition.Symbol = "TEST_DEF";
                break;
            case "variant":
                args.StripDefinition.Variant = 3;
                break;
            case "plugin":
                args.StripDefinition.Plugins = ["test.strip@2.0.0"];
                break;
        }
        BridgeCallException failure = Assert.Throws<BridgeCallException>(() => _harness.Bridge.Handlers[OperationNames.DevicePlaceExecute](args));
        Assert.Equal(ErrorReason.PlanStale, failure.Error.Reason);
        Assert.Empty(_placement.Calls);
    }

    private async Task<PackLibrary> StripPluginAsync(WriteToolHarness harness)
    {
        const string schemaPath = "knowledge/config_schema/defaults.json", layerPath = "knowledge/config_layer/defaults.json";
        string folder = Path.Combine(harness.Locations.Packs, "test.strip", "1.0.0");
        List<PackResource> resources = [];
        Add(schemaPath, ResourceKind.ConfigSchema, """{"items":{"defaults.strip_definition.symbol":{"type":"text","required":false,"allowedLayers":["base","company","cabinet_type","project","cabinet"]},"defaults.strip_definition.variant":{"type":"number","required":false,"allowedLayers":["base","company","cabinet_type","project","cabinet"]}}}""");
        Add(layerPath, ResourceKind.ConfigLayer, """{"layer":"company","schema":"config_schema/defaults","settings":{"defaults.strip_definition.symbol":{"default":"TEST_DEF"},"defaults.strip_definition.variant":{"default":3}}}""");
        var manifest = new PackManifest
        {
            SchemaVersion = 3,
            Id = "test.strip",
            Name = "Test strip",
            Version = "1.0.0",
            Compatibility = new(),
            Resources = [.. resources.OrderBy(item => item.Path, StringComparer.Ordinal)],
            Configuration = new() { Company = new() { Id = "defaults", Resources = [layerPath] } },
            Provenance = new() { CreatedAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), CreatedBy = "test", Inputs = [] }
        };
        byte[] bytes = PackJson.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), bytes);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(new() { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(bytes) }, [])));
        await new PluginStateStore(harness.Locations).WriteAsync(new(1, [new("test.strip", "1.0.0")], []), null, TestContext.Current.CancellationToken);
        harness.Bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = _store.Project };
        return new(harness.Locations, HostConfig.Defaults(harness.Locations), new AuditLog(harness.Locations.Audit, TimeProvider.System), TimeProvider.System);

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
