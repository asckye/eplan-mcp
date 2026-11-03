using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed partial class ApiTryToolTests : IDisposable
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
            using JsonDocument parameters = JsonDocument.Parse(args.Parameters);
            bool write = parameters.RootElement.TryGetProperty("value", out JsonElement value);
            WritePlan plan = _harness.Plan(_dispatchable ? [new() { Action = write ? "write" : "read", Kind = "unverified API", Name = Group, Property = "Page.Name() : System.String", NewValue = write ? value.GetRawText() : null }] : []);
            plan.Details = new() { ["status"] = _dispatchable ? "dispatchable" : "not_dispatchable_yet", ["unverified"] = "true", ["rawParameters"] = args.Parameters };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new ApiTryData { Value = "read\u202edata", Before = new() { ["snapshot:1"] = "a", ["file:Page.eod"] = "old" }, After = new() { ["snapshot:1"] = "b", ["file:Page.eod"] = "new" } };
    }
    public void Dispose() => _harness.Dispose();
    private ApiTryTools Tool(bool enabled = true) => new(_harness.Bridge, _harness.Pipeline, HostConfig.Defaults(_harness.Locations) with { ApiTry = enabled ? ActionRunSwitch.On : ActionRunSwitch.Off }, ApiIndex.Load(), _harness.Locations);
    private object Target(string page = "=A/1") => new { project = _harness.ProjectPath, page, type = 1, oid = 1, digest = new string('a', 64) };
    private string Parameters(string? value = null)
    {
        var parameters = new Dictionary<string, object> { ["target"] = Target() };
        if (value is not null)
        { parameters["value"] = value; }
        return JsonSerializer.Serialize(parameters);
    }
    private Task<CallToolResult> Call(bool dry = false, string? parameters = null, string group = Group) => Tool().TryAsync(_progress, group, "Line1", JsonDocument.Parse(parameters ?? Parameters()).RootElement, dry, cancellationToken: TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McpReadAndWritePreviewByDefault_OnlyExplicitFalseExecutes(bool write)
    {
        await PreviewProtocolHarness.RunAsync(_harness, async client =>
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            var arguments = new Dictionary<string, object?>
            {
                ["group"] = Group,
                ["project"] = "Line1",
                ["parameters"] = JsonSerializer.Deserialize<JsonElement>(Parameters(write ? "new name" : null)),
            };
            JsonElement omitted = WriteToolHarness.Structured(await client.CallToolAsync(ApiTryTools.Tool, arguments, cancellationToken: token));
            arguments["dry_run"] = true;
            JsonElement preview = WriteToolHarness.Structured(await client.CallToolAsync(ApiTryTools.Tool, arguments, cancellationToken: token));
            Assert.True(JsonElement.DeepEquals(omitted, preview));
            Assert.Equal(ExecutionState.NotStarted, omitted.GetProperty("execution").GetString());
            Assert.Equal("f1", omitted.GetProperty("fingerprint").GetString());
            Assert.NotEmpty(omitted.GetProperty("plannedChanges").EnumerateArray());
            Assert.Empty(_approval.Questions);
            Assert.Empty(_harness.Bridge.Writes);
            Assert.False(Directory.Exists(_harness.Locations.Exports));
            arguments["dry_run"] = false;
            JsonElement executed = WriteToolHarness.Structured(await client.CallToolAsync(ApiTryTools.Tool, arguments, cancellationToken: token));
            Assert.Equal(ExecutionState.Committed, executed.GetProperty("execution").GetString());
            Assert.Equal(omitted.GetProperty("fingerprint").GetString(), executed.GetProperty("fingerprint").GetString());
            Assert.Single(_approval.Questions);
            Assert.Single(_harness.Bridge.Writes);
        }, services => services.AddSingleton(HostConfig.Defaults(_harness.Locations) with { ApiTry = ActionRunSwitch.On }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OmittedAndTruePreviewTheSamePlan_ExplicitFalseExecutes(bool write)
    {
        JsonElement parameters = JsonSerializer.SerializeToElement(write
            ? new Dictionary<string, object> { ["target"] = Target(), ["value"] = "new name" }
            : new Dictionary<string, object> { ["target"] = Target() });
        ApiTryTools tool = Tool();
        JsonElement omitted = WriteToolHarness.Structured(await tool.TryAsync(_progress, Group, "Line1", parameters, cancellationToken: TestContext.Current.CancellationToken));
        JsonElement preview = WriteToolHarness.Structured(await tool.TryAsync(_progress, Group, "Line1", parameters, dry_run: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(omitted.GetRawText(), preview.GetRawText());
        Assert.Equal(ExecutionState.NotStarted, omitted.GetProperty("execution").GetString());
        Assert.Equal("f1", omitted.GetProperty("fingerprint").GetString());
        JsonElement change = Assert.Single(omitted.GetProperty("plannedChanges").EnumerateArray());
        Assert.Equal(write ? "write" : "read", change.GetProperty("action").GetString());
        Assert.Empty(omitted.GetProperty("changes").EnumerateArray());
        Assert.Contains("dry_run: false", omitted.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Exports));
        JsonElement executed = WriteToolHarness.Structured(await tool.TryAsync(_progress, Group, "Line1", parameters, dry_run: false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ExecutionState.Committed, executed.GetProperty("execution").GetString());
        Assert.Equal(omitted.GetProperty("plannedChanges").GetRawText(), executed.GetProperty("plannedChanges").GetRawText());
        Assert.Equal(omitted.GetProperty("fingerprint").GetString(), executed.GetProperty("fingerprint").GetString());
        Assert.Single(_approval.Questions);
        Assert.Single(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaticAndFileTrialsAlsoDefaultToPreview(bool file)
    {
        string group = file ? FileGroup : "O:Eplan.EplApi.DataModel.DocumentTypeManager.IsLogicalPageDocType";
        JsonElement parameters = file ? JsonDocument.Parse(FileParameters("definitions.xml")).RootElement : JsonSerializer.SerializeToElement(new { nPageType = "Circuit" });
        ApiTryTools tool = Tool();
        JsonElement omitted = WriteToolHarness.Structured(await tool.TryAsync(_progress, group, "Line1", parameters, cancellationToken: TestContext.Current.CancellationToken));
        JsonElement preview = WriteToolHarness.Structured(await tool.TryAsync(_progress, group, "Line1", parameters, dry_run: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(omitted.GetRawText(), preview.GetRawText());
        Assert.Equal(ExecutionState.NotStarted, omitted.GetProperty("execution").GetString());
        Assert.Equal("f1", omitted.GetProperty("fingerprint").GetString());
        Assert.NotEmpty(omitted.GetProperty("plannedChanges").EnumerateArray());
        Assert.Empty(omitted.GetProperty("files").EnumerateArray());
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Exports));
    }

    [Fact]
    public async Task RemovalUsesDestructiveApprovalAndDenialNeverExecutes()
    {
        const string group = "O:Eplan.EplApi.DataModel.Placement.Remove";
        _approved = false;
        _harness.Bridge.Handlers[OperationNames.ApiTryPlan] = a =>
        {
            WritePlan plan = _harness.Plan([new() { Action = "write", Kind = "unverified API", Name = group }]);
            plan.Details = new() { ["status"] = "dispatchable", ["lifecycle"] = "removes", ["rawParameters"] = ((ApiTryArgs)a!).Parameters };
            return plan;
        };
        Assert.Equal(ErrorReason.ApprovalDenied, WriteToolHarness.Error(await Call(group: group)).GetProperty("reason").GetString());
        ApprovalQuestion question = Assert.Single(_approval.Questions);
        Assert.Equal(ToolTier.Destructive, question.Tier);
        Assert.NotNull(question.BackupFolder);
        Assert.Contains(question.Changes, c => c.Contains("; removes; undo unverified", StringComparison.Ordinal));
        Assert.DoesNotContain(_harness.Bridge.Calls, c => c.Operation == OperationNames.ApiTryExecute);
    }

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
    [InlineData("{}")]
    [InlineData("{\"value\":\"new name\"}")]
    [InlineData("{\"target\":\"page-name\"}")]
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
        Assert.All(_harness.Bridge.Writes, w => Assert.Equal(TimeSpan.FromSeconds(120), w.Limit));
        string audit = File.ReadAllText(Assert.Single(Directory.GetFiles(_harness.Locations.Audit, "*.jsonl")));
        Assert.Contains("\"unverified\":true", audit, StringComparison.Ordinal);
        Assert.Contains("observations", audit, StringComparison.Ordinal);
        Assert.Contains("snapshot:1", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovalDisplaysEntireEscapedRawTextAndSignatureWithoutTruncation()
    {
        string raw = "{ \"target\": " + JsonSerializer.Serialize(Target(new string('x', 1000) + "\n\u202e")) + " }";
        WriteToolHarness.Structured(await Call(parameters: raw));
        ApprovalQuestion question = Assert.Single(_approval.Questions);
        foreach (string culture in new[] { "en-US", "zh-CN" })
        {
            ApprovalAskArgs view = ApprovalTexts.For(question, CultureInfo.GetCultureInfo(culture));
            Assert.StartsWith("read unverified API " + Group, view.Changes[0], StringComparison.Ordinal);
            Assert.Contains("Page.Name", view.Changes[0], StringComparison.Ordinal);
            Assert.Contains("unverified", view.Changes[0], StringComparison.Ordinal);
            Assert.All(view.Changes, line => Assert.InRange(line.Length, 1, 400));
            string shown = string.Concat(view.Changes.Skip(1).Select(line => line["parameters (JSON text): ".Length..]));
            Assert.Equal(raw, JsonSerializer.Deserialize<string>(shown));
        }
    }

    [Fact]
    public async Task WriteApprovalAndAuditNameTheWriteAndRequestedValue()
    {
        WriteToolHarness.Structured(await Call(parameters: Parameters("new name")));
        ApprovalQuestion question = Assert.Single(_approval.Questions);
        Assert.StartsWith("write unverified API " + Group + " = \"new name\"", Assert.Single(question.Changes), StringComparison.Ordinal);
        foreach (string culture in new[] { "en-US", "zh-CN" })
        { Assert.StartsWith("write unverified API " + Group + " = \"new name\"", ApprovalTexts.For(question, CultureInfo.GetCultureInfo(culture)).Changes[0], StringComparison.Ordinal); }
    }

    [Fact]
    public async Task PropertyBeforeAfterIsReportedFirst_EvenWhenEqualOrManySnapshotsChange()
    {
        const string key = "property:" + Group;
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new ApiTryData
        {
            Value = "same",
            Before = new() { [key] = "\"same\"" },
            After = Enumerable.Range(0, 25).ToDictionary(i => "snapshot:" + i.ToString(CultureInfo.InvariantCulture), _ => "changed")
                .Append(new KeyValuePair<string, string>(key, "\"same\"")).ToDictionary(),
        };
        JsonElement result = WriteToolHarness.Structured(await Call(parameters: Parameters("same")));
        JsonElement first = result.GetProperty("changes")[0];
        Assert.Equal(key, first.GetProperty("key").GetString());
        Assert.Equal(first.GetProperty("before").GetString(), first.GetProperty("after").GetString());
        Assert.Equal(20, result.GetProperty("changes").GetArrayLength());
        Assert.Equal(26, result.GetProperty("totalChanges").GetInt32());
        Assert.True(result.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DryRunOrNotDispatchableNeverAsksOrExecutes(bool dry)
    {
        _dispatchable = dry;
        JsonElement result = WriteToolHarness.Structured(await Call(dry));
        Assert.Equal(ExecutionState.NotStarted, result.GetProperty("execution").GetString());
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        if (!dry)
        { Assert.Equal("not_dispatchable_yet", result.GetProperty("status").GetString()); }
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
    [InlineData("timeout", false)]
    [InlineData("timeout", true)]
    [InlineData("modal", false)]
    [InlineData("modal", true)]
    public async Task UnknownOutcomeSuspendsProjectAndInstanceAndNeverResends(string cause, bool write)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.OutcomeUnknown, Execution = ExecutionState.Unknown, Message = cause };
        JsonElement error = WriteToolHarness.Error(await Call(parameters: write ? Parameters("new name") : null));
        Assert.Equal(ExecutionState.Unknown, error.GetProperty("execution").GetString());
        Assert.Single(_harness.Bridge.Writes);
        Assert.NotEmpty(SuspensionStore.For(_harness.Locations).All());
        string audit = File.ReadAllText(Assert.Single(Directory.GetFiles(_harness.Locations.Audit, "*.jsonl")));
        Assert.Contains("writes_suspended", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnedMemberBudgetFailureWithSuccessfulRollbackDoesNotSuspendFurtherWrites()
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.Timeout, Execution = ExecutionState.RolledBack, Message = "The member returned over budget; rollback succeeded." };
        JsonElement error = WriteToolHarness.Error(await Call(parameters: Parameters("new name")));
        Assert.Equal((ErrorReason.Timeout, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(SuspensionStore.For(_harness.Locations).All());
        _harness.Bridge.WriteFailure = null;
        Assert.Equal(ExecutionState.Committed, WriteToolHarness.Structured(await Call()).GetProperty("execution").GetString());
        Assert.Equal(2, _harness.Bridge.Writes.Count);
        Assert.Equal(2, _approval.Questions.Count);
    }

    [Fact]
    public async Task ObjectMethodResultReturnsTheExactDiscoveryLocator()
    {
        var locator = new DrawingLocator { Project = _harness.ProjectPath, Page = "=A/1", Type = 2, Oid = 12, Digest = new string('a', 64) };
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new ApiTryData { Locator = locator };
        JsonElement value = WriteToolHarness.Structured(await Call(group: "O:Eplan.EplApi.HEServices.SelectionSet.GetSelectedObject",
            parameters: JsonSerializer.Serialize(new { target = Target(page: ""), bFirst = true }))).GetProperty("value");
        Assert.Equal(JsonSerializer.SerializeToElement(locator, EplanMcp.Host.HostJson.Options).GetRawText(), value.GetRawText());
    }

    [Fact]
    public async Task LifecycleFactoryRequiresEveryApprovalAndReportsUnverifiedUndoExpectation()
    {
        const string factory = "O:Eplan.EplApi.DataModel.Graphics.Line.Create";
        _harness.Bridge.Handlers[OperationNames.ApiTryPlan] = a =>
        {
            var args = (ApiTryArgs)a!;
            WritePlan plan = _harness.Plan([new() { Action = "write", Kind = "unverified API", Name = factory, Property = "Line.Create(Page)" }]);
            plan.Details = new() { ["status"] = "dispatchable", ["unverified"] = "true", ["lifecycle"] = "creates", ["rawParameters"] = args.Parameters };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new ApiTryData { UndoExpected = true };
        var locator = new { project = _harness.ProjectPath, page = "", type = 1, oid = 1, digest = new string('a', 64) };
        string parameters = JsonSerializer.Serialize(new { page = locator });
        Assert.True(WriteToolHarness.Structured(await Call(group: factory, parameters: parameters)).GetProperty("undoExpected").GetBoolean());
        WriteToolHarness.Structured(await Call(group: factory, parameters: parameters));
        Assert.Equal(2, _approval.Questions.Count);
        Assert.All(_approval.Questions, q => Assert.Contains(q.Changes, c => c.Contains("; creates; undo unverified", StringComparison.Ordinal)));
        _approved = false;
        Assert.Equal(ErrorReason.ApprovalDenied, WriteToolHarness.Error(await Call(group: factory, parameters: parameters)).GetProperty("reason").GetString());
        Assert.Equal(2, _harness.Bridge.Writes.Count);
    }
    [Theory]
    [InlineData("Graphics.Line", false)]
    [InlineData("Graphics.Line", true)]
    [InlineData("ProtectedGroup", false)]
    public async Task FactoryRejectsExistingReceiverBeforeBridgeOrApproval(string type, bool handle)
    {
        object receiver = handle ? new { handle = "h:" + new string('a', 32), type = "Eplan.EplApi.DataModel.Graphics.Line" } : Target();
        _harness.Bridge.Handlers.Clear();
        JsonElement error = WriteToolHarness.Error(await Call(group: "O:Eplan.EplApi.DataModel." + type + ".Create", parameters: JsonSerializer.Serialize(new { target = receiver, page = Target() })));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Equal(ExecutionState.NotStarted, error.GetProperty("execution").GetString());
        Assert.Contains("Omit target for factory creation", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(_approval.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task RecoveredCommitDoesNotInventReadback()
    {
        _harness.Bridge.WriteRecovered = true;
        JsonElement result = WriteToolHarness.Structured(await Call());
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("after").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("undoExpected").ValueKind);
    }

    [Fact]
    public async Task SettingsEnumerationStillRequiresApprovalForEachCallAndPreservesPagingJson()
    {
        const string settings = "O:Eplan.EplApi.Base.SettingNode.GetListOfNodes";
        string raw = JsonSerializer.Serialize(new
        {
            target = new { project = _harness.ProjectPath, page = "", type = 1, oid = 1, digest = new string('a', 64) },
            node = "USER",
            offset = 0,
            limit = 200,
        });
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new ApiTryData
        { Value = JsonSerializer.Serialize(new { items = Enumerable.Repeat(new string('a', 100), 200), total = 200, nextOffset = (int?)null }) };
        for (int i = 0; i < 2; i++)
        {
            JsonElement result = WriteToolHarness.Structured(await Call(parameters: raw, group: settings));
            using JsonDocument page = JsonDocument.Parse(result.GetProperty("value").GetString()!);
            Assert.InRange(page.RootElement.GetProperty("items").GetArrayLength(), 1, 199);
            Assert.True(page.RootElement.GetProperty("nextOffset").GetInt32() > 0);
        }
        Assert.Equal(2, _approval.Questions.Count);
        Assert.Equal(2, _harness.Bridge.Writes.Count);
    }
}
