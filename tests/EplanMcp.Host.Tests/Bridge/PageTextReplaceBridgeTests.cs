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
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>Real write pipeline, audit files, router and both serializers; transport and native adapters are faked.</summary>
[Trait("Size", "Medium")]
public sealed class PageTextReplaceBridgeTests : IDisposable
{
    private readonly WriteToolHarness _harness = new();
    private readonly PageTextStore _store = new();
    private readonly OperationRouter _router;

    public PageTextReplaceBridgeTests()
    {
        _store.Project.Path = _harness.ProjectPath;
        _router = new OperationRouter(
            [new PageTextReplacePlanOperation(_store, _store), new PageTextReplaceExecuteOperation(_store, _store, _store)],
            new Dispatcher(), null, new Log(), TimeSpan.FromSeconds(1));
        _harness.Bridge.Handlers[OperationNames.PageTextReplacePlan] = args => Invoke<WritePlan>(OperationNames.PageTextReplacePlan, args, "plan");
        _harness.Bridge.Handlers[OperationNames.PageTextReplaceExecute] = args => Invoke<PageTextReplaceData>(OperationNames.PageTextReplaceExecute, args, _harness.Bridge.Writes[^1].RequestId);
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Execute_VerifiedChanges_RoundTripAndCommitWithOneAuditPair()
    {
        WriteOutcome<PageTextReplaceData> outcome = await RunAsync();

        Assert.Equal(ExecutionState.Committed, outcome.Execution);
        Assert.Equal("Line1", outcome.Data!.Project);
        Assert.Equal(["Drive Drive", "Drive", "Drive"], outcome.Data.Changes.Select(c => c.After));
        Assert.Equal(["Begin AI: eplan_page_text_replace", "Commit", "CloseUndo", "Dispose"], _store.Calls);
        Assert.Equal(["execution_started", "execution_result"], Records().Select(r => r["kind"]!.GetValue<string>()));
        Assert.Equal(ExecutionState.Committed, Records()[^1]["execution"]!.GetValue<string>());
        Assert.Single(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task DryRun_ReturnsCompletePlanWithoutWritingOrAudit()
    {
        string before = Inventory();

        WriteOutcome<PageTextReplaceData> outcome = await RunAsync(dryRun: true);

        Assert.Equal(ExecutionState.NotStarted, outcome.Execution);
        Assert.Equal(3, outcome.Plan.PageTextChanges!.Count);
        Assert.Null(outcome.Data);
        Assert.Empty(_store.Calls);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Audit));
        Assert.Equal(before, Inventory());
    }

    [Fact]
    public async Task Execute_StalePlan_RefusesBeforeTransactionAndAuditsNotStarted()
    {
        _harness.Bridge.DuringWrite = _ => _store.Pages.Remove(PageTextStore.OtherPage);

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => RunAsync())).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(ExecutionState.NotStarted, Records()[^1]["execution"]!.GetValue<string>());
        Assert.Empty(_store.Calls);
        Assert.False(_store.Pages.ContainsKey(PageTextStore.OtherPage));
    }

    [Fact]
    public async Task Execute_ReadbackMismatch_RollsBackAndAuditsResult()
    {
        string before = Inventory();
        _store.Readback = t => t.Oid == 3 ? new(t.Oid, t.Identifier, [new("en_US", "Wrong")]) : t;

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => RunAsync())).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal("en_US", error.Metadata!["language"]);
        Assert.Equal(ExecutionState.RolledBack, Records()[^1]["execution"]!.GetValue<string>());
        Assert.Equal(before, Inventory());
        Assert.DoesNotContain("Commit", _store.Calls);
    }

    [Fact]
    public async Task Execute_WriteThrows_ReportsRollbackWithoutInternalDetails()
    {
        string before = Inventory();
        _store.WriteFailure = new InvalidOperationException("private native details");

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => RunAsync())).Error;

        Assert.Equal((ErrorReason.Internal, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.DoesNotContain("private native", error.Message, StringComparison.Ordinal);
        Assert.Equal(ExecutionState.RolledBack, Records()[^1]["execution"]!.GetValue<string>());
        Assert.Equal(before, Inventory());
    }

    private Task<WriteOutcome<PageTextReplaceData>> RunAsync(bool dryRun = false)
    {
        PageTextReplaceArgs args = PageTextStore.Args();
        var request = new WriteRequest<PageTextReplaceArgs, PageTextReplaceArgs>(
            PageTextReplaceExecuteOperation.Tool, ToolTier.Write, OperationNames.PageTextReplacePlan, OperationNames.PageTextReplaceExecute,
            args, plan => new PageTextReplaceArgs
            {
                Project = args.Project,
                Pages = args.Pages,
                Find = args.Find,
                Replace = args.Replace,
                Language = args.Language,
                CaseSensitive = args.CaseSensitive,
                MaxChanges = args.MaxChanges,
                Fingerprint = plan.Fingerprint,
            }, dryRun, TimeSpan.FromSeconds(5));
        return _harness.Pipeline.RunAsync<PageTextReplaceArgs, PageTextReplaceArgs, PageTextReplaceData>(request, TestContext.Current.CancellationToken);
    }

    private T Invoke<T>(string operation, object? args, string requestId)
    {
        JToken wire = JToken.Parse(JsonSerializer.Serialize(args, HostJson.Options));
        try
        {
            JToken result = _router.Invoke(operation, wire, requestId);
            return JsonSerializer.Deserialize<T>(result.ToString(), HostJson.Options)!;
        }
        catch (BridgeErrorException ex)
        {
            throw new BridgeCallException(JsonSerializer.Deserialize<BridgeError>(JsonSerializer.Serialize(ex.Error, HostJson.Options), HostJson.Options)!);
        }
    }

    private List<JsonObject> Records() =>
        [.. Directory.EnumerateFiles(_harness.Locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];

    private string Inventory() => JsonSerializer.Serialize(_store.Pages);

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
