using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class CabinetToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-cabinets-").FullName);
    private readonly ManualTime _clock = new(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new() { Status = new([], new() { Eplan = new() { ProcessId = 42, Version = "2026.0.3" }, Operations = [OperationNames.ProjectSnapshot] }, null) };
    private readonly FakeApprovalChannel _channel = new("fake", ApprovalDecision.Granted);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Project => PlanSnapshotHashes.ProjectKey(ContextTestSnapshot.ProjectPath);
    private AuditLog Audit => new(_locations.Audit, _clock);
    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), Audit, _clock);
    private string BindingFile => Path.Combine(Packs.Workspaces.FolderOf(Project), ProjectBinding.FileName);
    private BindingService Service(FakeApprovalChannel? channel = null) => new(Packs, Audit,
        new ApprovalService([channel ?? _channel], Packs.Config, _clock, NullLogger<ApprovalService>.Instance), _bridge);

    public CabinetToolTests()
    {
        _bridge.Handlers[OperationNames.ProjectSnapshot] = args => ContextTestSnapshot.Data((ProjectSnapshotArgs)args!);
        _bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Name = "Line1", Path = Project } };
    }

    [Fact]
    public async Task InstalledConfigFixture_PassesPackCheck()
    {
        Pack("one", "one-only");
        PackCheckResult check = await PackVerifier.CheckDirectoryAsync(Packs.Installed.FolderOf("one", "1.0.0"), Token);
        Assert.True(check.Ok, string.Join("\n", check.Problems));
    }

    [Fact]
    public async Task UnboundList_IsVirtualAndNeverCreatesFiles()
    {
        JsonElement result = Data(await List());
        Assert.Equal(0, result.GetProperty("revision").GetInt32());
        Assert.Equal("default", Assert.Single(result.GetProperty("cabinets").EnumerateArray()).GetProperty("id").GetString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_locations.Root));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task TwoCabinets_ContextSelectsOnlyItsObjectsConfigurationAndTrace()
    {
        ProjectBinding binding = Seed();
        byte[] before = File.ReadAllBytes(BindingFile);
        foreach ((string cabinet, string page, string expected, string other) in new[] { ("a1", "+A1/1", "one-only", "ten-only"), ("a10", "+A10/2", "ten-only", "one-only") })
        {
            JsonElement result = Data(await Context(cabinet));
            Assert.Equal(cabinet, result.GetProperty("cabinet").GetString());
            Assert.Equal(page, Assert.Single(result.GetProperty("coverage").GetProperty("pages").EnumerateArray()).GetProperty("name").GetString());
            Assert.Equal(expected, Assert.Single(result.GetProperty("config").EnumerateArray()).GetProperty("value").GetString());
            Assert.DoesNotContain(other, result.GetRawText(), StringComparison.Ordinal);
            Assert.Equal(2, result.GetProperty("objects").GetArrayLength());
            Assert.All(result.GetProperty("config")[0].GetProperty("trace").EnumerateArray(), trace => Assert.True(trace.GetProperty("source").TryGetProperty("sha256", out _) || trace.GetProperty("source").TryGetProperty("bindingRevision", out _)));
            if (cabinet == "a10")
            { Assert.Contains(result.GetProperty("config")[0].GetProperty("trace").EnumerateArray(), trace => trace.GetProperty("pointer").GetString() == "/cabinets/1/values/mode/default"); }
            Assert.Equal(binding.Revision, result.GetProperty("contextRef").GetProperty("bindingRevision").GetInt32());
            ContextBuilderReadTests.AssertSources(result);
        }
        Assert.Equal(ErrorReason.InvalidArgument, Error(await Context(null)).GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.InvalidArgument, Error(await Context("absent")).GetProperty("reason").GetString());
        Assert.Equal(before, File.ReadAllBytes(BindingFile));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task ConfigFailure_TraceNeverContainsAnotherCabinetsValues()
    {
        ProjectBinding binding = Seed();
        Seed(binding with { ProjectValues = JsonNode.Parse("""{"unknown":{"default":true}}""")!.AsObject() });
        JsonElement error = Error(await Context("a1"));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.DoesNotContain("ten-only", error.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("a10", error.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_PagesAndRejectsCursorAfterBindingChange()
    {
        ProjectBinding binding = Seed();
        JsonElement first = Data(await List(limit: 1));
        string cursor = first.GetProperty("nextCursor").GetString()!;
        JsonElement second = Data(await List(limit: 1, cursor: cursor));
        Assert.Equal("a10", Assert.Single(second.GetProperty("cabinets").EnumerateArray()).GetProperty("id").GetString());
        Seed(binding with { Revision = 8 });
        Assert.Equal(ErrorReason.InvalidArgument, Error(await List(cursor: cursor)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task List_BudgetCountsBothJsonCopies_AndDoesNotPublishPartialTrace()
    {
        Seed();
        CallToolResult call = await List(tokens: 5000);
        JsonElement result = Data(call);
        Assert.True(ResultShaper.ResultTokens(result.GetRawText()) + ResultShaper.EstimateTokens(((TextContentBlock)call.Content[0]).Text) <= 5000);
        Assert.Equal(ErrorReason.InvalidArgument, Error(await List(tokens: 1)).GetProperty("reason").GetString());
        foreach (JsonElement cabinet in result.GetProperty("cabinets").EnumerateArray())
        { Assert.NotEmpty(cabinet.GetProperty("config")[0].GetProperty("trace").EnumerateArray()); }
    }

    [Fact]
    public async Task DryRun_ValidatesLocksAndConfigurationWithoutConfirmationOrFiles()
    {
        ProjectBinding proposal = Proposal();
        JsonElement result = Data(await Bind(proposal, dryRun: true));
        Assert.True(result.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, result.GetProperty("binding").GetProperty("revision").GetInt32());
        Assert.Empty(_channel.Questions);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_locations.Root));
    }

    [Theory]
    [InlineData(false, ErrorReason.ApprovalDenied)]
    [InlineData(true, ErrorReason.ApprovalExpired)]
    public async Task LockChange_RefusalLeavesBindingAbsent(bool expired, string reason)
    {
        PackReference pin = Pack("one", "one-only");
        ProjectBinding proposal = Proposal() with { PackRoots = [new() { Id = pin.Id, Version = pin.Version }] };
        var channel = new FakeApprovalChannel("fake", expired ? ApprovalDecision.Expired : ApprovalDecision.Denied);
        Assert.Equal(reason, Error(await Bind(proposal, channel: channel)).GetProperty("reason").GetString());
        Assert.Single(channel.Questions);
        Assert.False(File.Exists(BindingFile));
    }

    [Fact]
    public async Task LockChanges_ConfirmButValueAndMappingChangesOnlyAudit_AndReplayIsIdempotent()
    {
        PackReference pin = Pack("one", "one-only");
        ProjectBinding proposal = Proposal() with { PackRoots = [new() { Id = pin.Id, Version = pin.Version }] };
        JsonElement preview = Data(await Bind(proposal, dryRun: true));
        Assert.Single(preview.GetProperty("binding").GetProperty("locks").EnumerateArray());
        Assert.Empty(_channel.Questions);
        ProjectBinding first = ReadBinding(Data(await Bind(proposal)));
        Assert.Single(_channel.Questions);
        Assert.Equal(ToolTier.Write, _channel.Questions[0].Tier);
        Assert.Contains("local binding", ApprovalTexts.For(_channel.Questions[0], System.Globalization.CultureInfo.GetCultureInfo("en")).Note, StringComparison.Ordinal);
        ProjectBinding updated = first with { Cabinets = [first.Cabinets[0] with { Title = "renamed", Values = JsonNode.Parse("""{"mode":{"default":"changed"}}""")!.AsObject() }] };
        ProjectBinding second = ReadBinding(Data(await Bind(updated)));
        Assert.Equal(2, second.Revision);
        Assert.Single(_channel.Questions);
        Assert.Equal(2, ReadBinding(Data(await Bind(second))).Revision);
        Assert.Single(_channel.Questions);
        ProjectBinding removed = second with { PackRoots = [], Cabinets = [ContextBuilder.VirtualCabinet] };
        Assert.Empty(ReadBinding(Data(await Bind(removed))).Locks);
        Assert.Equal(2, _channel.Questions.Count);
        Assert.NotEmpty(Directory.GetFiles(_locations.Audit, "*.jsonl"));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task InvalidConfig_StaleRevisionAndTamperedPack_DoNotChangeBinding()
    {
        ProjectBinding binding = Seed();
        byte[] before = File.ReadAllBytes(BindingFile);
        ProjectBinding invalid = binding with { Cabinets = [binding.Cabinets[0] with { Values = JsonNode.Parse("""{"unknown":{"default":true}}""")!.AsObject() }, binding.Cabinets[1]] };
        Assert.Equal(ErrorReason.InvalidArgument, Error(await Bind(invalid)).GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.PlanStale, Error(await Bind(binding with { Revision = 1 })).GetProperty("reason").GetString());
        File.AppendAllText(Path.Combine(_locations.Packs, "one", "1.0.0", "knowledge", "config_layer", "company.json"), " ");
        Assert.Equal(ErrorReason.PackInvalid, Error(await Context("a1")).GetProperty("reason").GetString());
        Assert.Equal(before, File.ReadAllBytes(BindingFile));
        Assert.Empty(_channel.Questions);
    }

    [Theory]
    [InlineData("[{\"id\":\"a\",\"layers\":[],\"values\":{},\"structure\":{\"all\":true},\"extra\":true}]", "{}")]
    [InlineData("[]", "{\"power\":{\"default\":1e100}}")]
    [InlineData("[]", "{\"x\":{\"default\":1,\"default\":2}}")]
    public async Task StrictMembersFiniteNumbersAndDuplicates_AreRejected(string cabinets, string values)
    {
        JsonElement error = Error(await CabinetTools.BindAsync(Packs, _bridge, Service(), Project, 0, Parse(cabinets), Parse(values), Parse("[]"), dry_run: true, cancellationToken: Token));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Empty(_channel.Questions);
        Assert.False(File.Exists(BindingFile));
    }

    [Fact]
    public async Task McpContract_BindPreviewListAndContextUseTheRegisteredSchemas()
    {
        Seed();
        var toServer = new Pipe();
        var toClient = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_locations);
        builder.Services.AddEplanMcp().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(_bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: Token);
            var tools = await client.ListToolsAsync(cancellationToken: Token);
            Assert.NotNull(Assert.Single(tools, t => t.Name == CabinetTools.BindTool).ProtocolTool.OutputSchema);
            JsonElement listed = Data(await client.CallToolAsync(CabinetTools.ListTool, new Dictionary<string, object?> { ["project"] = "Line1" }, cancellationToken: Token));
            Assert.Equal(2, listed.GetProperty("total").GetInt32());
            CallToolResult scoped = await client.CallToolAsync(ContextTools.GetTool, new Dictionary<string, object?> { ["project"] = "Line1", ["cabinet"] = "a1", ["scope"] = new { } }, cancellationToken: Token);
            Assert.DoesNotContain("ten-only", Data(scoped).GetRawText(), StringComparison.Ordinal);
            ProjectBinding binding = (await Packs.Workspaces.ReadAsync(Project, Token))!;
            CallToolResult preview = await client.CallToolAsync(CabinetTools.BindTool, new Dictionary<string, object?>
            {
                ["project"] = "Line1",
                ["expected_revision"] = 7,
                ["cabinets"] = JsonSerializer.SerializeToElement(binding.Cabinets, PackJson.Options),
                ["project_values"] = new { },
                ["pack_roots"] = Array.Empty<object>(),
                ["dry_run"] = true
            }, cancellationToken: Token);
            Assert.True(Data(preview).GetProperty("dryRun").GetBoolean());
            Assert.Empty(_channel.Questions);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private Task<CallToolResult> Context(string? cabinet) => ContextTools.GetAsync(_bridge, Packs, Parse("{}"), project: Project, cabinet: cabinet, max_tokens: 20000, cancellationToken: Token);
    private Task<CallToolResult> List(int limit = 50, string? cursor = null, int tokens = 5000) => CabinetTools.ListAsync(Packs, _bridge, Project, limit, cursor, tokens, cancellationToken: Token);
    private Task<CallToolResult> Bind(ProjectBinding binding, bool dryRun = false, FakeApprovalChannel? channel = null) => CabinetTools.BindAsync(Packs, _bridge, Service(channel), Project, binding.Revision,
        JsonSerializer.SerializeToElement(binding.Cabinets, PackJson.Options), JsonSerializer.SerializeToElement(binding.ProjectValues, PackJson.Options), JsonSerializer.SerializeToElement(binding.PackRoots, PackJson.Options), dryRun, cancellationToken: Token);
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static JsonElement Data(CallToolResult call) => ContextToolTests.Data(call);
    private static JsonElement Error(CallToolResult call) => ContextToolTests.Error(call);
    private static ProjectBinding ReadBinding(JsonElement result) => PackJson.ReadBinding(System.Text.Encoding.UTF8.GetBytes(result.GetProperty("binding").GetRawText()));
    private static ProjectBinding Proposal() => new() { BindingVersion = 2, Project = Project, Revision = 0, Cabinets = [ContextBuilder.VirtualCabinet], PackRoots = [], ProjectValues = new() };
    private ProjectBinding Seed()
    {
        PackReference one = Pack("one", "one-only"), ten = Pack("ten", "ten-only");
        ProjectBinding binding = Proposal() with
        {
            Revision = 7,
            Locks = [one, ten],
            Cabinets = [Cabinet("a1", "+A1", "one"), Cabinet("a10", "+A10", "ten") with { Values = JsonNode.Parse("""{"mode":{"default":"ten-only"}}""")!.AsObject() }]
        };
        Seed(binding);
        return binding;
    }
    private static CabinetBinding Cabinet(string id, string location, string pack) => new()
    {
        Id = id,
        Layers = [$"{pack}@1.0.0:config_layer/company"],
        Values = new(),
        Structure = new() { ["prefixes"] = new JsonArray(new JsonObject { ["location"] = location }) }
    };
    private void Seed(ProjectBinding binding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BindingFile)!);
        File.WriteAllBytes(BindingFile, PackJson.Serialize(binding));
    }
    private PackReference Pack(string id, string value)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_locations.Packs, id, "1.0.0")).FullName;
        var resources = new List<PackResource>();
        Add("config_schema", "settings", ResourceKind.ConfigSchema, Parse("""{"items":{"mode":{"type":"text","default":"base","required":true,"allowedLayers":["base","company","cabinet_type","project","cabinet"]}}}"""));
        Add("config_layer", "company", ResourceKind.ConfigLayer, JsonSerializer.SerializeToElement(new { layer = "company", schema = "config_schema/settings", settings = new { mode = new { @default = value } } }));
        var manifest = new PackManifest
        {
            SchemaVersion = 2,
            Id = id,
            Name = id,
            Version = "1.0.0",
            Compatibility = new(),
            Resources = [.. resources.OrderBy(r => r.Path, StringComparer.Ordinal)],
            Provenance = new() { CreatedAt = _clock.GetUtcNow(), CreatedBy = "eplanmcp test", Inputs = [] }
        };
        byte[] bytes = PackJson.Serialize(manifest);
        var pin = new PackReference { Id = id, Version = "1.0.0", Digest = PackDigest.Compute(bytes) };
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), bytes);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(pin, [])));
        return pin;
        void Add(string kind, string name, ResourceKind resourceKind, JsonElement spec)
        {
            byte[] content = PackJson.Serialize(new { apiVersion = "eplanmcp.pack/v2", kind, metadata = new { name }, spec });
            string path = $"knowledge/{kind}/{name}.json";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, path))!);
            File.WriteAllBytes(Path.Combine(folder, path), content);
            resources.Add(new() { Path = path, Kind = resourceKind, Storage = ResourceStorage.Bundled, Bytes = content.Length, Files = 1, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
        }
    }
    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);
}
