using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PageUpdateFrameProtocolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);
    private readonly PageFrameStore _store = new();
    private Action? _beforeWrite;

    [Fact]
    public async Task DryRun_RealMcp_CombinesPropertiesPerPageWithoutApprovalAuditOrWrite()
    {
        await WithClient(async client =>
        {
            JsonElement result = Data(await Call(client, dryRun: true));
            JsonElement page = Assert.Single(result.GetProperty("pages").EnumerateArray());
            Assert.Equal(("Main", "Power", "Frame_B", "Frame_A"), (page.GetProperty("description").GetString(), page.GetProperty("previous").GetString(),
                page.GetProperty("plotFrame").GetString(), page.GetProperty("previousPlotFrame").GetString()));
            Assert.Equal(("not_started", 1), (result.GetProperty("execution").GetString(), result.GetProperty("unchanged").GetInt32()));
            Assert.Empty(_store.Calls);
            Assert.Empty(_harness.Bridge.Writes);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
        });
    }

    [Fact]
    public async Task FrameOnlyCommit_AutoAllowedInScratch_AuditsBothValuesAndReadsBack()
    {
        await WithClient(async client =>
        {
            Dictionary<string, object?> args = Args();
            args["pages"] = new[] { new { name = PageFrameStore.First, plot_frame = "Frame_B" } };
            JsonElement result = Data(await client.CallToolAsync(PageWriteTools.UpdateTool, args, cancellationToken: Token));
            Assert.Equal(("committed", "AI: eplan_page_update", 0), (result.GetProperty("execution").GetString(), result.GetProperty("undo").GetString(), result.GetProperty("unchanged").GetInt32()));
            Assert.Equal(JsonValueKind.Null, result.GetProperty("pages")[0].GetProperty("description").ValueKind);
            Assert.Equal("Frame_B", result.GetProperty("pages")[0].GetProperty("plotFrame").GetString());
            Assert.Single(_harness.Bridge.Writes);
            Assert.Empty(_store.DescriptionWrites);
            JsonObject started = Records(_harness).Single(r => r["kind"]!.GetValue<string>() == "execution_started");
            Assert.Equal("Frame_B", started["arguments"]!["pages"]![0]!["plotFrame"]!.GetValue<string>());
            string line = Assert.Single(started["changes"]!.AsArray())!.GetValue<string>();
            Assert.Contains("Frame_A", line, StringComparison.Ordinal);
            Assert.Contains("Frame_B", line, StringComparison.Ordinal);
            Assert.Contains(PageFrameStore.First, line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AlreadyInheritedFrameMatches_NoApprovalAuditTransactionOrFixedSetting()
    {
        await WithClient(async client =>
        {
            Dictionary<string, object?> args = Args();
            args["pages"] = new[] { new { name = PageFrameStore.First, plot_frame = "Frame_A" } };
            JsonElement result = Data(await client.CallToolAsync(PageWriteTools.UpdateTool, args, cancellationToken: Token));
            Assert.Equal(("not_started", 1, 0), (result.GetProperty("execution").GetString(), result.GetProperty("unchanged").GetInt32(), result.GetProperty("pages").GetArrayLength()));
            Assert.Empty(_store.Calls);
            Assert.Null(_store.Frames[PageFrameStore.First].Setting);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
        }, outside: true);
    }

    [Fact]
    public async Task UnknownFrame_RealMcp_HasAvailableMetadataWithoutAudit()
    {
        await WithClient(async client =>
        {
            Dictionary<string, object?> args = Args();
            args["pages"] = new[] { new { name = PageFrameStore.First, plot_frame = "missing" } };
            JsonElement error = Error(await client.CallToolAsync(PageWriteTools.UpdateTool, args, cancellationToken: Token));
            Assert.Equal(("INVALID_ARGUMENT", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            Assert.Equal("Frame_A\nFrame_B", error.GetProperty("metadata").GetProperty("available").GetString());
            Assert.Empty(_store.Calls);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
        });
    }

    [Fact]
    public async Task Stale_RealMcp_RefusesBeforeTransaction()
    {
        _beforeWrite = () => _store.Frames[PageFrameStore.First] = new("page:1", "Frame_A", "Frame_A");
        await WithClient(async client =>
        {
            JsonElement error = Error(await Call(client));
            Assert.Equal(("PLAN_STALE", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            Assert.Empty(_store.Calls);
            Assert.Empty(_store.FrameWrites);
        });
    }

    [Fact]
    public async Task CapAndMissingProperties_RealMcp_RefuseWithoutWrite()
    {
        await WithClient(async client =>
        {
            Dictionary<string, object?> args = Args();
            args["pages"] = Enumerable.Repeat(new { name = PageFrameStore.First, plot_frame = "Frame_B" }, 51).ToArray();
            Assert.Equal("INVALID_ARGUMENT", Error(await client.CallToolAsync(PageWriteTools.UpdateTool, args, cancellationToken: Token)).GetProperty("reason").GetString());
            args["pages"] = new[] { new { name = PageFrameStore.First } };
            Assert.Equal("INVALID_ARGUMENT", Error(await client.CallToolAsync(PageWriteTools.UpdateTool, args, cancellationToken: Token)).GetProperty("reason").GetString());
            Assert.Empty(_store.FrameWrites);
            Assert.Empty(_store.Calls);
        });
    }

    [Fact]
    public async Task OutsideScratch_DenyPolicyAllowsDryRunAndRefusesCommit()
    {
        await WithClient(async client =>
        {
            Assert.Equal("not_started", Data(await Call(client, dryRun: true)).GetProperty("execution").GetString());
            Assert.Equal("POLICY_DENIED", Error(await Call(client)).GetProperty("reason").GetString());
            Assert.Empty(_store.FrameWrites);
        }, outside: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OutsideScratch_ApprovalShowsPageAndOldToNewFrame(bool approve)
    {
        var channel = new FakeApprovalChannel("engineer", approve ? ApprovalDecision.Granted : ApprovalDecision.Denied);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Approve, channel);
        await WithClient(async client =>
        {
            CallToolResult result = await Call(client);
            ApprovalQuestion question = Assert.Single(channel.Questions);
            string row = question.Changes.Single(c => c.Contains("plot_frame", StringComparison.Ordinal));
            Assert.Contains(PageFrameStore.First, row, StringComparison.Ordinal);
            Assert.Contains("Frame_A", row, StringComparison.Ordinal);
            Assert.Contains("Frame_B", row, StringComparison.Ordinal);
            Assert.Equal(2, question.Plan.Changes.Count);
            if (approve)
            {
                Assert.Equal("committed", Data(result).GetProperty("execution").GetString());
                Assert.Single(_store.FrameWrites);
            }
            else
            {
                Assert.Equal("APPROVAL_DENIED", Error(result).GetProperty("reason").GetString());
                Assert.Empty(_store.FrameWrites);
            }
        }, outside: true, harness);
    }

    [Fact]
    public async Task RecoveredCommit_GroupsBothChangesAndKeepsPageCounts()
    {
        _harness.Bridge.WriteRecovered = true;
        await WithClient(async client =>
        {
            JsonElement result = Data(await Call(client));
            Assert.True(result.GetProperty("recovered").GetBoolean());
            Assert.Equal(1, result.GetProperty("unchanged").GetInt32());
            JsonElement page = Assert.Single(result.GetProperty("pages").EnumerateArray());
            Assert.Equal("Frame_B", page.GetProperty("plotFrame").GetString());
            Assert.Equal("Main", page.GetProperty("description").GetString());
        });
    }

    [Fact]
    public async Task DescriptionOnlyResponse_KeepsExistingFieldsSummaryAndNoFrameReads()
    {
        await WithClient(async client =>
        {
            Dictionary<string, object?> args = Args(dryRun: true);
            args["pages"] = new[] { new { name = PageFrameStore.First, description = "Main" } };
            CallToolResult response = await client.CallToolAsync(PageWriteTools.UpdateTool, args, cancellationToken: Token);
            JsonElement page = Assert.Single(Data(response).GetProperty("pages").EnumerateArray());
            Assert.Equal(["name", "description", "previous"], page.EnumerateObject().Select(p => p.Name));
            Assert.Equal("Dry run: would update 1 page. Nothing was changed.", ((TextContentBlock)response.Content[0]).Text);
            Assert.Equal(0, _store.DiscoveryCalls);
        });
    }

    public void Dispose()
    {
        _store.Dispose();
        _harness.Dispose();
    }

    private async Task WithClient(Func<McpClient, Task> check, bool outside = false, WriteToolHarness? harness = null)
    {
        WriteToolHarness environment = harness ?? _harness;
        _store.Project.Path = outside ? Path.Combine(environment.Locations.Root, "outside", "Line1") : environment.ProjectPath;
        var router = new OperationRouter(
            [new PageUpdatePlanOperation(_store, _store, _store, () => _store), new PageUpdateExecuteOperation(_store, _store, _store, _store, () => _store)],
            new Dispatcher(), null, new Log(), TimeSpan.FromSeconds(1));
        environment.Bridge.Handlers[OperationNames.PageUpdatePlan] = args => Invoke<WritePlan>(router, OperationNames.PageUpdatePlan, args, "plan");
        environment.Bridge.Handlers[OperationNames.PageUpdateExecute] = args => Invoke<PageUpdateData>(router, OperationNames.PageUpdateExecute, args, environment.Bridge.Writes[^1].RequestId);
        environment.Bridge.DuringWrite = _ => _beforeWrite?.Invoke();
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(environment.Locations);
        builder.Services.AddSingleton(environment.Pipeline);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(environment.Bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            await check(client);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static T Invoke<T>(OperationRouter router, string operation, object? args, string requestId)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(router.Invoke(operation, JToken.Parse(JsonSerializer.Serialize(args, HostJson.Options)), requestId).ToString(), HostJson.Options)!;
        }
        catch (BridgeErrorException ex)
        {
            throw new BridgeCallException(ex.Error);
        }
    }

    private static Dictionary<string, object?> Args(bool dryRun = false) => new()
    {
        ["project"] = "Line1",
        ["pages"] = new[] { new { name = PageFrameStore.First, description = (string?)"Main", plot_frame = "Frame_B" }, new { name = PageFrameStore.Second, description = (string?)null, plot_frame = "Frame_B" } },
        ["language"] = "en_US",
        ["dry_run"] = dryRun,
    };

    private static Task<CallToolResult> Call(McpClient client, bool dryRun = false) => client.CallToolAsync(PageWriteTools.UpdateTool, Args(dryRun), cancellationToken: Token).AsTask();
    private static JsonElement Data(CallToolResult result) => WriteToolHarness.Structured(result);
    private static JsonElement Error(CallToolResult result) => WriteToolHarness.Error(result);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static List<JsonObject> Records(WriteToolHarness harness) =>
        [.. Directory.EnumerateFiles(harness.Locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];

    private sealed class Dispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            try
            {
                return DispatchResult.Completed(work());
            }
            catch (Exception ex)
            {
                return DispatchResult.Faulted(ex);
            }
        }
    }

    private sealed class Log : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}
