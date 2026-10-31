using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ApiTryToolTests : IDisposable
{
    private readonly FakeApprovalChannel _approval;
    private readonly WriteToolHarness _harness;
    private bool _approved = true;
    private bool _dispatchable = true;
    private const string Group = "O:Eplan.EplApi.DataModel.Page.Name";
    private readonly RecordedProgress _progress = new();
    public ApiTryToolTests()
    {
        _approval = new("eplan_dialog", null) { Answers = _ => _approved ? ApprovalDecision.Granted : ApprovalDecision.Denied };
        _harness = new(_approval);
        _harness.Bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Name = "Line1", Path = _harness.ProjectPath } };
        _harness.Bridge.Handlers[OperationNames.ApiTryPlan] = a =>
        {
            var args = (ApiTryArgs)a!;
            WritePlan plan = _harness.Plan(_dispatchable ? [new() { Action = "try", Kind = "unverified API", Name = Group, Property = "Page.Name() : System.String", NewValue = args.Parameters }] : []);
            plan.Details = new() { ["status"] = _dispatchable ? "dispatchable" : "not_dispatchable_yet", ["unverified"] = "true", ["rawParameters"] = args.Parameters };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new ApiTryData { Value = "read\u202edata", Before = new() { ["snapshot:1"] = "a", ["file:Page.eod"] = "old" }, After = new() { ["snapshot:1"] = "b", ["file:Page.eod"] = "new" } };
    }
    public void Dispose() => _harness.Dispose();
    private ApiTryTools Tool(bool enabled = true) => new(_harness.Bridge, _harness.Pipeline, HostConfig.Defaults(_harness.Locations) with { ApiTry = enabled ? ActionRunSwitch.On : ActionRunSwitch.Off }, ApiIndex.Load());
    private Task<CallToolResult> Call(bool dry = false, string? parameters = null, string group = Group) => Tool().TryAsync(_progress, group, "Line1", JsonDocument.Parse(parameters ?? "{}").RootElement, dry, TestContext.Current.CancellationToken);

    [Fact]
    public async Task OffUnknownExcludedAndMemberIdsNeverContactBridge()
    {
        Assert.Equal(ErrorReason.PolicyDenied, WriteToolHarness.Error(await Tool(false).TryAsync(_progress, Group, "Line1", JsonSerializer.SerializeToElement(new { }), cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
        string excluded = ApiIndex.Load().Data.Entries.First(e => e.Row.ExcludedReason is not null && e.Row.Group.StartsWith("O:", StringComparison.Ordinal)).Row.Group;
        foreach (string group in new[] { "System.IO.File.Delete", "P:Eplan.EplApi.DataModel.Page.Name", excluded })
        { Assert.Equal(ErrorReason.PolicyDenied, WriteToolHarness.Error(await Call(group: group)).GetProperty("reason").GetString()); }
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"target\":null}")]
    [InlineData("{\"action\":{\"name\":\"ExecuteScript\"}}")]
    [InlineData("{\"p\":[]}")]
    [InlineData("{\"p\":1,\"p\":2}")]
    public async Task InvalidParametersAreRejectedBeforeBridge(string json)
    {
        Assert.Equal(ErrorReason.InvalidArgument, WriteToolHarness.Error(await Call(parameters: json)).GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutsideScratchNeverAsksOrExecutes_EvenWithApprovePolicy(bool dry)
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "real", "Line1");
        Assert.Equal(ErrorReason.PolicyDenied, WriteToolHarness.Error(await Call(dry)).GetProperty("reason").GetString());
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Single(_harness.Bridge.Calls);
    }

    [Fact]
    public async Task EveryExecutionAsks_AndRecordsUnverifiedSnapshotChanges()
    {
        JsonElement result = WriteToolHarness.Structured(await Call());
        Assert.True(result.GetProperty("unverified").GetBoolean());
        Assert.Equal(2, result.GetProperty("totalChanges").GetInt32());
        Assert.NotEqual(result.GetProperty("before").GetString(), result.GetProperty("after").GetString());
        Assert.Equal("readdata", result.GetProperty("value").GetString());
        WriteToolHarness.Structured(await Call());
        Assert.Equal(2, _approval.Questions.Count);
        Assert.All(_harness.Bridge.Writes, w => Assert.Equal(TimeSpan.FromSeconds(30), w.Limit));
        string audit = File.ReadAllText(Assert.Single(Directory.GetFiles(_harness.Locations.Audit, "*.jsonl")));
        Assert.Contains("\"unverified\":true", audit, StringComparison.Ordinal);
        Assert.Contains("observations", audit, StringComparison.Ordinal);
        Assert.Contains("snapshot:1", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovalDisplaysEntireEscapedRawTextAndSignatureWithoutTruncation()
    {
        string raw = "{ \"target\": \"" + new string('x', 1000) + "\\n\\u202e\" }";
        WriteToolHarness.Structured(await Call(parameters: raw));
        ApprovalQuestion question = Assert.Single(_approval.Questions);
        foreach (string culture in new[] { "en-US", "zh-CN" })
        {
            ApprovalAskArgs view = ApprovalTexts.For(question, CultureInfo.GetCultureInfo(culture));
            Assert.Contains(Group, view.Changes[0], StringComparison.Ordinal);
            Assert.Contains("Page.Name", view.Changes[0], StringComparison.Ordinal);
            Assert.Contains("unverified", view.Changes[0], StringComparison.Ordinal);
            Assert.All(view.Changes, line => Assert.InRange(line.Length, 1, 400));
            string shown = string.Concat(view.Changes.Skip(1).Select(line => line["parameters (JSON text): ".Length..]));
            Assert.Equal(raw, JsonSerializer.Deserialize<string>(shown));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DryRunOrNotDispatchableNeverAsksOrExecutes(bool dry)
    {
        _dispatchable = dry;
        JsonElement result = WriteToolHarness.Structured(await Call(dry));
        Assert.Equal(ExecutionState.NotStarted, result.GetProperty("execution").GetString());
        Assert.Empty(_approval.Questions); Assert.Empty(_harness.Bridge.Writes);
        if (!dry) { Assert.Equal("not_dispatchable_yet", result.GetProperty("status").GetString()); }
    }

    [Fact]
    public async Task DeniedOrUnwritableAuditExecutesNothing()
    {
        _approved = false;
        Assert.Equal(ErrorReason.ApprovalDenied, WriteToolHarness.Error(await Call()).GetProperty("reason").GetString());
        _approved = true;
        Directory.Delete(_harness.Locations.Audit, true);
        File.WriteAllText(_harness.Locations.Audit, "blocked");
        Assert.Equal(ErrorReason.AuditUnavailable, WriteToolHarness.Error(await Call()).GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("modal")]
    public async Task UnknownOutcomeSuspendsProjectAndInstanceAndNeverResends(string cause)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.OutcomeUnknown, Execution = ExecutionState.Unknown, Message = cause };
        JsonElement error = WriteToolHarness.Error(await Call());
        Assert.Equal(ExecutionState.Unknown, error.GetProperty("execution").GetString());
        Assert.Single(_harness.Bridge.Writes);
        Assert.NotEmpty(SuspensionStore.For(_harness.Locations).All());
        string audit = File.ReadAllText(Assert.Single(Directory.GetFiles(_harness.Locations.Audit, "*.jsonl")));
        Assert.Contains("writes_suspended", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecoveredCommitDoesNotInventReadback()
    {
        _harness.Bridge.WriteRecovered = true;
        JsonElement result = WriteToolHarness.Structured(await Call());
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("after").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("value").ValueKind);
    }
}
