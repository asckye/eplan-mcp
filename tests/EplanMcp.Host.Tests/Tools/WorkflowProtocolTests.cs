using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class WorkflowProtocolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-workflow-").FullName);
    private readonly FakeBridgeClient _bridge = new();
    private bool _changed;
    private bool _changeDuringCompile;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public WorkflowProtocolTests()
    {
        _bridge.Status = new([], new()
        {
            Eplan = new() { Version = "2026.0.3", Variant = "Pro Panel" },
            Operations = [OperationNames.ProjectSnapshot, OperationNames.DeviceList, OperationNames.PlanFunctions, OperationNames.ProjectBackup,
                OperationNames.PageCreatePlan, OperationNames.PageCreateExecute, OperationNames.PageUpdatePlan, OperationNames.PageUpdateExecute,
                OperationNames.MacroInsertPlan, OperationNames.MacroInsertExecute, OperationNames.PlanMacroInspect],
        }, null);
        _bridge.Handlers[OperationNames.ProjectSnapshot] = args => ContextTestSnapshot.Data((ProjectSnapshotArgs)args!, changed: _changed);
        _bridge.Handlers[OperationNames.DeviceList] = _ => new ListData<DeviceListItem> { Project = "Line1", Total = 0 };
        _bridge.Handlers[OperationNames.PageCreatePlan] = _ =>
        {
            _changed = _changeDuringCompile;
            WritePlan plan = PlanStepCompilationData.Plan("page.create");
            plan.ProjectPath = ContextTestSnapshot.ProjectPath;
            return plan;
        };
        _bridge.Handlers[OperationNames.PlanMacroInspect] = _ => new PlanMacroInspectData
        {
            Sha256 = new string('a', 64),
            ObjectCount = 2,
            Functions = [new() { Kind = "function", Category = "Coil", Name = "+-Z?", VisibleName = "-Z?", Main = true, StructureSegments = ["+"] }],
        };
    }

    [Theory]
    [InlineData("page.update")]
    [InlineData("macro.insert")]
    public async Task Compile_ThroughMcp_PublishesHashEvidenceAuditAndTrustedOwner(string op)
    {
        await WithClient(async (client, provider) =>
        {
            CallToolResult call = await client.CallToolAsync(WorkflowTools.PlanTool, Request(op), cancellationToken: Token);
            JsonElement data = ContextToolTests.Data(call);
            string id = data.GetProperty("planId").GetString()!;
            PlanStore store = provider.GetRequiredService<PlanStore>();
            PlanRecord record = (await store.ReadAsync(id, Token))!;
            Assert.Equal(PlanCanonical.Hash(record.Definition), data.GetProperty("planHash").GetString());
            Assert.Equal(2, record.Definition.Steps.Length);
            Assert.NotNull(record.Definition.Steps[1].Prediction);
            Assert.Equal(JsonValueKind.Null, record.Definition.Inputs.Steps[0].Args.GetProperty("language").ValueKind);
            Assert.True(provider.GetRequiredService<PlanTrust>().Matches(record.Definition, record.Definition.Owner, record.PlanHash));
            Assert.False(provider.GetRequiredService<PlanTrust>().Matches(record.Definition, new("other", "session"), record.PlanHash));
            string changes = await store.ReadChangesAsync(id, Token);
            Assert.Equal(PlanStore.Hash(Encoding.UTF8.GetBytes(changes)), record.Definition.Digests.ChangesDigest);
            string evidenceHash = record.Definition.ContextRef![7..];
            Assert.NotEmpty(await store.ReadEvidenceAsync(id, evidenceHash, Token));
            // Sources are only the objects a step checks or writes (and resources); the snapshot is the evidence above.
            HashSet<(string, int, long)> checkedObjects = [.. record.Definition.Steps.SelectMany(s => s.Preconditions.OfType<PlanObjectHash>())
                .Select(p => (p.Locator.Kind, p.Locator.Type, p.Locator.Oid))];
            Assert.All(record.Definition.Sources.Where(s => s.Kind is not null), s => Assert.Contains((s.Kind!, s.Type!.Value, s.Oid!.Value), checkedObjects));
            if (op == "macro.insert")
            {
                Assert.Contains(record.Definition.Sources, s => s.FileHash == new string('a', 64) && s.ResourcePath == "Motor.ema");
            }
            string audit = await File.ReadAllTextAsync(Directory.GetFiles(_locations.Audit, "*.jsonl").Single(), Token);
            JsonElement entry = JsonSerializer.Deserialize<JsonElement>(audit);
            Assert.Equal("plan_compiled", entry.GetProperty("kind").GetString());
            Assert.Equal(record.PlanHash, entry.GetProperty("planHash").GetString());
            Assert.Equal(id, entry.GetProperty("planId").GetString());
            Assert.Equal(record.Definition.Owner.SessionId, entry.GetProperty("owner").GetProperty("sessionId").GetString());
            Assert.True(JsonElement.DeepEquals(data, ContextToolTests.Parse(((TextContentBlock)call.Content[1]).Text)));
            Assert.Equal("Compiled a plan with 2 steps; no project data was changed.", ((TextContentBlock)call.Content[0]).Text);
            Assert.Equal("eplanmcp://plans/" + id, Assert.IsType<ResourceLinkBlock>(call.Content[2]).Uri);
            Assert.Empty(_bridge.Writes);
            Assert.DoesNotContain(_bridge.Calls, c => c.Operation.EndsWith(".execute", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Compile_ProjectChangesDuringPlanning_ReturnsStaleConflictsAndPublishesNothing()
    {
        _changeDuringCompile = true;
        await AssertError(ErrorReason.PlanStale, error => Assert.NotEmpty(error.GetProperty("metadata").GetProperty("conflicts").EnumerateArray()));
        Assert.Equal(3, _bridge.Calls.Count(c => c.Operation == OperationNames.ProjectSnapshot));
    }

    [Fact]
    public async Task Compile_ProjectNotOpen_ReturnsReadErrorAndPublishesNothing()
    {
        _bridge.Failure = new() { Reason = ErrorReason.ProjectNotOpen, Message = "Open the project and compile again." };
        await AssertError(ErrorReason.ProjectNotOpen);
    }

    [Fact]
    public async Task Compile_InvalidArguments_ListsJsonPointersBeforeReading()
    {
        await WithClient(async (client, _) =>
        {
            var request = new Dictionary<string, object?> { ["project"] = "Line1", ["steps"] = new[] { new { op = "module.insert", args = new { } } } };
            JsonElement error = ContextToolTests.Error(await client.CallToolAsync(WorkflowTools.PlanTool, request, cancellationToken: Token));
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
            Assert.Equal("/steps/0/op", error.GetProperty("metadata").GetProperty("pointer").GetString());
            Assert.Single(error.GetProperty("metadata").GetProperty("violations").EnumerateArray());
            Assert.Empty(_bridge.Calls);
        });
    }

    [Fact]
    public async Task Compile_AuditUnavailable_PublishesNothing()
    {
        await File.WriteAllTextAsync(_locations.Audit, "blocked", Token);
        await AssertError(ErrorReason.AuditUnavailable);
    }

    [Fact]
    public async Task Compile_CapacityFull_ReturnsResourceExhaustedAndKeepsExistingRecords()
    {
        string quarantine = Directory.CreateDirectory(Path.Combine(_locations.State, "plans", "quarantine")).FullName;
        using (var file = new FileStream(Path.Combine(quarantine, "retained.bin"), FileMode.CreateNew))
        {
            file.SetLength(PlanCapacity.Total);
        }
        await AssertError(ErrorReason.ResourceExhausted, error =>
        {
            Assert.Equal("plans.bytes", error.GetProperty("metadata").GetProperty("resource").GetString());
            Assert.False(error.GetProperty("retryable").GetBoolean());
        });
        Assert.True(File.Exists(Path.Combine(quarantine, "retained.bin")));
    }

    [Fact]
    public async Task Compile_MacroChangesBetweenInspectionAndPlanning_ReturnsStale()
    {
        int inspections = 0;
        _bridge.Handlers[OperationNames.PlanMacroInspect] = _ => new PlanMacroInspectData
        {
            Sha256 = new string(++inspections == 1 ? 'a' : 'b', 64),
            ObjectCount = 1,
        };
        await WithClient(async (client, _) =>
        {
            JsonElement error = ContextToolTests.Error(await client.CallToolAsync(WorkflowTools.PlanTool, Request("macro.insert"), cancellationToken: Token));
            Assert.Equal(ErrorReason.PlanStale, error.GetProperty("reason").GetString());
            Assert.Equal("macro", Assert.Single(error.GetProperty("metadata").GetProperty("conflicts").EnumerateArray()).GetProperty("scope").GetString());
            string root = Path.Combine(_locations.State, "plans");
            Assert.Empty(Directory.Exists(root) ? Directory.GetFiles(root, "*.json") : []);
        });
    }

    [Theory]
    [InlineData("default")]
    [InlineData("panel")]
    public async Task Compile_ExistingBinding_UsesRealDefaultRevisionOrRefusesOtherCabinet(string cabinet)
    {
        string key = PlanSnapshotHashes.ProjectKey(ContextTestSnapshot.ProjectPath);
        var workspaces = new WorkspaceStore(_locations.Workspaces, new PackStore(_locations.Packs));
        string folder = Directory.CreateDirectory(workspaces.FolderOf(key)).FullName;
        var binding = new ProjectBinding
        {
            BindingVersion = 1,
            Project = key,
            Revision = 42,
            Cabinets = [new() { Id = cabinet }],
        };
        await File.WriteAllBytesAsync(Path.Combine(folder, ProjectBinding.FileName), PackJson.Serialize(binding), Token);
        await WithClient(async (client, provider) =>
        {
            CallToolResult call = await client.CallToolAsync(WorkflowTools.PlanTool, Request(), cancellationToken: Token);
            if (cabinet != "default")
            {
                Assert.Equal(ErrorReason.CapabilityMissing, ContextToolTests.Error(call).GetProperty("reason").GetString());
                return;
            }
            JsonElement data = ContextToolTests.Data(call);
            Assert.Equal(42, data.GetProperty("bindingRevision").GetInt64());
            Assert.Matches("^[a-f0-9]{64}$", data.GetProperty("lockDigest").GetString());
            PlanRecord record = (await provider.GetRequiredService<PlanStore>().ReadAsync(data.GetProperty("planId").GetString()!, Token))!;
            Assert.All(record.Definition.Steps, step => Assert.Contains(step.Preconditions, p => p is PlanBinding { Revision: 42 }));
        });
    }

    private async Task AssertError(string reason, Action<JsonElement>? inspect = null)
    {
        await WithClient(async (client, _) =>
        {
            CallToolResult call = await client.CallToolAsync(WorkflowTools.PlanTool, Request(), cancellationToken: Token);
            JsonElement error = ContextToolTests.Error(call);
            Assert.Equal(reason, error.GetProperty("reason").GetString());
            Assert.False(error.TryGetProperty("execution", out JsonElement _));
            inspect?.Invoke(error);
            string root = Path.Combine(_locations.State, "plans");
            Assert.Empty(Directory.Exists(root) ? Directory.GetFiles(root, "*.json") : []);
        });
    }

    internal static Dictionary<string, object?> Request(string op = "page.update")
    {
        object reference = new { from_step = 1, page = "+A1/3" };
        object args = op == "page.update" ? new { pages = new[] { new { name = reference, description = "New" } } }
            : new { page = reference, macro = "Motor.ema", x = 40.5, y = 280.25 };
        return new()
        {
            ["project"] = "Line1",
            ["steps"] = new object[]
            {
                new { op = "page.create", args = new { pages = new[] { new { name = "+A1/3", description = "Power" } } } },
                new { op, args },
            },
        };
    }

    private async Task WithClient(Func<McpClient, IServiceProvider, Task> check)
    {
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
            await check(client, host.Services);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);
}
