using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Packs;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed partial class WorkflowCabinetTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-workflow-cabinet-").FullName);
    private readonly FakeBridgeClient _bridge = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private AuditLog Audit => new(_locations.Audit, TimeProvider.System);
    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), Audit, TimeProvider.System);
    private string BindingFile => Path.Combine(Packs.Workspaces.FolderOf(ContextTestSnapshot.ProjectPath), ProjectBinding.FileName);
    private string Folder => Packs.Installed.FolderOf("acme.panels", "1.0.0");

    public WorkflowCabinetTests()
    {
        _bridge.Status = new([], new()
        {
            Eplan = new() { Version = "2026.0.3", Variant = "Pro Panel" },
            Operations = [OperationNames.ProjectList, OperationNames.PlanNames, OperationNames.ProjectSnapshot, OperationNames.PlanFunctions,
                OperationNames.ProjectBackup, OperationNames.PageCreatePlan, OperationNames.PageCreateExecute, OperationNames.PageUpdatePlan, OperationNames.PageUpdateExecute, OperationNames.DeviceList],
        }, null);
        _bridge.Projects.Add(new() { Name = "Line1", Path = ContextTestSnapshot.ProjectPath });
        _bridge.Handlers[OperationNames.ProjectSnapshot] = args => ContextTestSnapshot.Data((ProjectSnapshotArgs)args!);
        _bridge.Handlers[OperationNames.DeviceList] = _ => new ListData<DeviceListItem> { Project = "Line1", Total = 0 };
        _bridge.Handlers[OperationNames.PlanNames] = _ =>
        {
            PlanOccupiedName[] names = [.. ContextTestSnapshot.Data(new()).Pages.SelectMany(p => p.Objects).Select(o => new PlanOccupiedName(o.Kind, o.Name!))];
            return new PlanNamesData
            {
                Project = "Line1",
                ProjectPath = ContextTestSnapshot.ProjectPath,
                ProjectGuid = "sample@1",
                Complete = true,
                Names = [.. names.Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name })],
                NamesHash = PlanSnapshotHashes.Names(ContextTestSnapshot.ProjectPath, names)
            };
        };
        _bridge.Handlers[OperationNames.PageCreatePlan] = args => new WritePlan
        {
            Project = "Line1",
            ProjectPath = ContextTestSnapshot.ProjectPath,
            Fingerprint = "runtime",
            Changes = [.. ((PageCreateArgs)args!).Pages.Select(p => new PlannedChange { Action = "create", Kind = "page", Name = p.Name, NewValue = p.Description })],
        };
        _bridge.Handlers[OperationNames.PageUpdatePlan] = args => new WritePlan
        {
            Project = "Line1",
            ProjectPath = ContextTestSnapshot.ProjectPath,
            Fingerprint = "runtime",
            Changes = [.. ((PageUpdateArgs)args!).Pages.Select(p => new PlannedChange { Action = "update", Kind = "page", Name = p.Name, Property = "description (en_US)", NewValue = p.Description })],
        };
    }

    [Fact]
    public async Task LockedWorkflow_SameInputsDifferentCabinets_FixesDifferentConfigStepsAndCompleteProvenance()
    {
        await Seed();
        await WithClient(async (client, store) =>
        {
            PlanRecord a = await Record(client, store, Request("a1"));
            PlanRecord b = await Record(client, store, Request("a10"));
            Assert.NotEqual(a.PlanHash, b.PlanHash);
            Assert.NotEqual(a.Definition.Engineering!.ConfigDigest, b.Definition.Engineering!.ConfigDigest);
            Assert.Equal("dol", a.Definition.Steps[0].Args.GetProperty("pages")[0].GetProperty("description").GetString());
            Assert.Equal("star_delta", b.Definition.Steps[0].Args.GetProperty("pages")[0].GetProperty("description").GetString());
            Assert.Equal("+A1/3", a.Definition.Steps[0].Args.GetProperty("pages")[0].GetProperty("name").GetString());
            Assert.Equal("+A10/3", b.Definition.Steps[0].Args.GetProperty("pages")[0].GetProperty("name").GetString());
            Assert.Equal((2, "3", "a1", 7L), (a.SchemaVersion, a.Definition.CompilerVersion, a.Definition.Cabinet, a.Definition.BindingRevision));
            Assert.Equal("+A1", a.Definition.Engineering.Acceptance.GetProperty("cabinetScope").GetProperty("prefixes")[0].GetProperty("location").GetString());
            PlanConfigValue mode = Assert.Single(a.Definition.Engineering.ConfigValuesAndTrace, c => c.Key == "mode");
            Assert.Equal(["base", "company", "project", "cabinet"], mode.Trace.Select(t => t.Layer));
            Assert.Contains(mode.Trace, t => t.Source.TryGetProperty("bindingRevision", out JsonElement revision) && revision.GetInt64() == 7);
            Assert.Contains(mode.Trace, t => t.Source.TryGetProperty("resource", out JsonElement resource) && resource.GetString() == "acme.panels@1.0.0:config_layer/company");
            Assert.Equal("acme.panels@1.0.0:workflow/create", a.Definition.Engineering.WorkflowRefAndInputs.GetProperty("workflow").GetString());
            Assert.All(a.Definition.Resources, r => Assert.Contains(a.Definition.Sources, s => s.ResourcePath == r.ResourcePath && s.ExactVersion == "1.0.0" && s.FileHash == r.FileHash && s.ManifestDigest == r.ManifestDigest));
            Assert.Equal(PlanCanonical.Hash(a.Definition), a.PlanHash);
            Assert.True(PlanApplyChecks.UsesLocalBaseline("3"));
        });
        Assert.Empty(_bridge.Writes);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation.EndsWith(".execute", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("a1", "+A10/3")]
    [InlineData("a1", "+A2/3")]
    [InlineData("a10", "+A1/3")]
    public async Task InlineStep_OutsideOrUnassignedCabinet_IsRejectedWithoutPublishing(string cabinet, string page)
    {
        await Seed();
        await WithClient(async (client, _) =>
        {
            var request = new Dictionary<string, object?> { ["project"] = "Line1", ["cabinet"] = cabinet, ["steps"] = new[] { new { op = "page.create", args = new { pages = new[] { new { name = page } } } } } };
            JsonElement error = ContextToolTests.Error(await client.CallToolAsync(WorkflowTools.PlanTool, request, cancellationToken: Token));
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
            Assert.Equal("/cabinet", error.GetProperty("metadata").GetProperty("pointer").GetString());
            AssertNoPlan();
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    public async Task MultipleCabinets_MissingOrUnknownSelection_IsInvalid(string? cabinet)
    {
        await Seed();
        await WithClient(async (client, _) =>
        {
            Assert.Equal(ErrorReason.InvalidArgument, ContextToolTests.Error(await client.CallToolAsync(WorkflowTools.PlanTool, Request(cabinet), cancellationToken: Token)).GetProperty("reason").GetString());
            AssertNoPlan();
        });
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("unknown")]
    [InlineData("no_review")]
    [InlineData("accepted")]
    public async Task MandatoryRules_FailedUnknownOrUnpromoted_CannotPublish(string condition)
    {
        await Seed(condition);
        await WithClient(async (client, _) =>
        {
            JsonElement error = ContextToolTests.Error(await client.CallToolAsync(WorkflowTools.PlanTool, Update(), cancellationToken: Token));
            Assert.Equal(condition is "no_review" or "accepted" ? ErrorReason.PackInvalid : ErrorReason.CapabilityMissing, error.GetProperty("reason").GetString());
            AssertNoPlan();
        });
    }

    [Fact]
    public async Task PromotedRule_RecordsExactVersionDigestAndLocalConfirmationWithoutDevFolder()
    {
        string review = await Seed("pass");
        await WithClient(async (client, store) =>
        {
            PlanRecord record = await Record(client, store, Update());
            JsonElement digest = record.Definition.Engineering!.RuleDigests.GetProperty("acme.panels@1.0.0:rule/motor");
            Assert.Equal(review, digest.GetProperty("reviewDigest").GetString());
            Assert.Equal("pass", Assert.Single(record.Definition.Engineering.Acceptance.GetProperty("rules").EnumerateArray()).GetProperty("status").GetString());
            Assert.Contains(record.Definition.Resources, r => r.FileHash == digest.GetProperty("sha256").GetString() && r.ResourcePath == "knowledge/rule/motor.json");
        });
        Assert.False(Directory.Exists(Packs.Dev.FolderOf("acme.panels")));
    }

    [Fact]
    public async Task OldInlineCall_UnboundAndEmptyLegacyBinding_HaveIdenticalDefinitionsExceptIdentityAndTime()
    {
        await WithClient(async (client, store) =>
        {
            var request = new Dictionary<string, object?> { ["project"] = "Line1", ["steps"] = new[] { new { op = "page.create", args = new { pages = new[] { new { name = "+A1/3" } } } } } };
            PlanRecord a = await Record(client, store, request);
            SaveBinding(new() { BindingVersion = 1, Project = PlanSnapshotHashes.ProjectKey(ContextTestSnapshot.ProjectPath), Revision = 42, Cabinets = [] });
            PlanRecord b = await Record(client, store, request);
            Assert.Equal((1, "2"), (a.SchemaVersion, a.Definition.CompilerVersion));
            Assert.Null(a.Definition.Engineering);
            Assert.Null(b.Definition.Engineering);
            Assert.Equal(a.PlanHash, PlanCanonical.Hash(b.Definition with { PlanId = a.Definition.PlanId, CreatedAt = a.Definition.CreatedAt, ExpiresAt = a.Definition.ExpiresAt }));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compilation_BindingOrLockedContentChanges_RefusesPublication(bool content)
    {
        await Seed();
        Func<object?, object> plan = _bridge.Handlers[OperationNames.PageCreatePlan];
        _bridge.Handlers[OperationNames.PageCreatePlan] = args =>
        {
            if (content)
            { File.AppendAllText(Path.Combine(Folder, "knowledge/config_schema/settings.json"), "\n"); }
            else
            {
                ProjectBinding binding = PackJson.ReadBinding(File.ReadAllBytes(BindingFile));
                SaveBinding(binding with { Revision = 8 });
            }
            return plan(args);
        };
        await WithClient(async (client, _) =>
        {
            JsonElement error = ContextToolTests.Error(await client.CallToolAsync(WorkflowTools.PlanTool, Request("a1"), cancellationToken: Token));
            Assert.Equal(content ? ErrorReason.PackInvalid : ErrorReason.PlanStale, error.GetProperty("reason").GetString());
            AssertNoPlan();
        });
    }

    [Theory]
    [InlineData("{\"power\":1e100}")]
    [InlineData("{\"power\":1,\"power\":2}")]
    [InlineData("{\"unknown\":1}")]
    public async Task WorkflowInputs_NonFiniteDuplicateOrUnknown_AreInvalid(string inputs)
    {
        await Seed();
        await WithClient(async (client, _) =>
        {
            var request = Request("a1");
            request["inputs"] = JsonDocument.Parse(inputs).RootElement.Clone();
            JsonElement error = ContextToolTests.Error(await client.CallToolAsync(WorkflowTools.PlanTool, request, cancellationToken: Token));
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
            AssertNoPlan();
        });
    }

    private static Dictionary<string, object?> Request(string? cabinet) => new() { ["project"] = "Line1", ["cabinet"] = cabinet, ["workflow"] = "acme.panels@1.0.0:workflow/create", ["inputs"] = new { } };
    private static Dictionary<string, object?> Update() => new() { ["project"] = "Line1", ["cabinet"] = "a1", ["steps"] = new[] { new { op = "page.update", args = new { pages = new[] { new { name = "+A1/1", description = "Changed" } } } } } };
    private static async Task<PlanRecord> Record(McpClient client, PlanStore store, Dictionary<string, object?> request)
    {
        JsonElement data = ContextToolTests.Data(await client.CallToolAsync(WorkflowTools.PlanTool, request, cancellationToken: Token));
        return (await store.ReadAsync(data.GetProperty("planId").GetString()!, Token))!;
    }
    private void AssertNoPlan() => Assert.Empty(Directory.Exists(Path.Combine(_locations.State, "plans")) ? Directory.GetFiles(Path.Combine(_locations.State, "plans"), "*.json") : []);
    private void SaveBinding(ProjectBinding binding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BindingFile)!);
        File.WriteAllBytes(BindingFile, PackJson.Serialize(binding));
    }
    private async Task<string> Seed(string? rule = null)
    {
        var resources = new List<PackResource>();
        Add(KnowledgeKind.ConfigSchema, "settings", """{"items":{"mode":{"type":"text","default":"base","required":true,"allowedLayers":["base","company","project","cabinet"]},"target":{"type":"text","required":true,"allowedLayers":["cabinet"]}}}""");
        Add(KnowledgeKind.ConfigLayer, "company", """{"layer":"company","schema":"config_schema/settings","settings":{"mode":{"default":"company"}}}""");
        Add(KnowledgeKind.Workflow, "create", """{"parameters":{"power":{"type":"number","required":false,"default":1}},"requiredCapabilities":["op:page.create.execute"],"steps":[{"op":"page.create","args":{"pages":[{"name":{"config":"target"},"description":{"config":"mode"}}]}}]}""");
        string reviewDigest = "";
        if (rule is not null)
        {
            AssetEntry candidate = ReviewFixtures.Entry(ReviewFixtures.Document());
            AddBytes(candidate.Resource, PackJson.Serialize(candidate.Document));
            AddBytes(ReviewFixtures.Guide.Resource, ReviewFixtures.GuideBytes);
            KnowledgeReview review = ReviewFixtures.Review(candidate, "promote");
            KnowledgeDocument document = KnowledgeDocument.Read(review.TargetContent!);
            document.Spec["scope"]!["kind"] = "page";
            document.Spec["check"] = JsonNode.Parse(rule == "fail" ? """{"literal":false}""" : rule == "unknown" ? """{"some":{"relation":"connection.start","where":{"literal":true}}}""" : """{"eq":[{"config":"mode"},{"literal":"dol"}]}""");
            if (rule == "unknown")
            {
                document.Spec["scope"]!["kind"] = "connection";
                document.Spec["check"] = JsonNode.Parse("""{"literal":true}""");
            }
            review = review with { TargetContent = PackJson.Serialize(document) };
            AddBytes(ReviewFixtures.Entry(document).Resource, review.TargetContent!);
            if (rule != "no_review")
            {
                KnowledgeReview recorded = rule == "accepted" ? ReviewFixtures.Review(candidate) : review;
                JsonObject record = KnowledgeReviewService.Bind(recorded, candidate, ReviewFixtures.Guide);
                record["kind"] = "knowledge_review";
                record["confirmation"] = "Granted";
                record["channel"] = "engineer";
                reviewDigest = await Audit.AppendAsync(record, Token);
            }
        }
        var manifest = new PackManifest
        {
            SchemaVersion = 2,
            Id = "acme.panels",
            Name = "ACME",
            Version = "1.0.0",
            Compatibility = new(),
            Resources = [.. resources.OrderBy(r => r.Path, StringComparer.Ordinal)],
            Rules = rule is null ? [] : [new() { Path = "knowledge/rule/motor.json", Standing = KnowledgeStanding.Official }],
            Provenance = new() { CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "eplanmcp test", Inputs = [] }
        };
        byte[] bytes = PackJson.Serialize(manifest);
        var pin = new PackReference { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(bytes) };
        File.WriteAllBytes(Path.Combine(Folder, PackManifest.FileName), bytes);
        File.WriteAllBytes(Path.Combine(Folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(pin, [])));
        SaveBinding(new()
        {
            BindingVersion = 2,
            Project = PlanSnapshotHashes.ProjectKey(ContextTestSnapshot.ProjectPath),
            Revision = 7,
            Locks = [pin],
            PackRoots = [new() { Id = pin.Id, Version = pin.Version }],
            ProjectValues = JsonNode.Parse("""{"mode":{"default":"project"}}""")!.AsObject(),
            Cabinets = [Cabinet("a1", "+A1", "dol"), Cabinet("a10", "+A10", "star_delta")]
        });
        return reviewDigest;
        void Add(KnowledgeKind kind, string name, string spec)
        {
            KnowledgeDocument document = new() { ApiVersion = KnowledgeDocument.V2ApiVersion, Kind = kind, Metadata = new() { Name = name }, Spec = JsonNode.Parse(spec)!.AsObject() };
            AddBytes(ReviewFixtures.Entry(document).Resource, PackJson.Serialize(document));
        }
        void AddBytes(PackResource resource, byte[] content)
        {
            string path = Path.Combine(Folder, resource.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
            resources.Add(resource);
        }
    }
    private static CabinetBinding Cabinet(string id, string location, string mode) => new()
    {
        Id = id,
        Layers = ["acme.panels@1.0.0:config_layer/company"],
        Values = JsonNode.Parse(JsonSerializer.Serialize(new { mode = new { @default = mode }, target = new { @default = location + "/3" } }))!.AsObject(),
        Structure = new() { ["prefixes"] = new JsonArray(new JsonObject { ["location"] = location }) }
    };
    private async Task WithClient(Func<McpClient, PlanStore, Task> check, Action<IServiceCollection>? configure = null)
    {
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_locations);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(_bridge);
        configure?.Invoke(builder.Services);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            await check(client, host.Services.GetRequiredService<PlanStore>());
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);
}
