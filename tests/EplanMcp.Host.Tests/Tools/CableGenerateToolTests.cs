using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>The real Destructive pipeline with a fake Bridge; no EPLAN or project files (tool spec 7).</summary>
[Trait("Size", "Medium")]
public sealed class CableGenerateToolTests : IDisposable
{
    private static readonly string[] _dryUnknown = ["after", "changes", "changedCount", "backup", "undo"];
    private static readonly string[] _recoveredUnknown = ["after", "changes", "changedCount", "undo"];
    private readonly WriteToolHarness _harness;
    private readonly FakeApprovalChannel _dialog;
    private readonly RecordedProgress _progress = new();
    private ApprovalDecision _decision = ApprovalDecision.Granted;
    private readonly CableGenerateData _data = new()
    {
        Project = "Line1",
        Before = Counts(),
        After = Counts(),
        ChangedCount = 0,
        Hint = "Readback covers the declared business fields; full selection effects remain unverified.",
    };

    public CableGenerateToolTests()
    {
        _dialog = new FakeApprovalChannel("eplan_dialog", null) { Answers = q => q.Release is null ? _decision : ApprovalDecision.Denied };
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.CableGeneratePlan] = _ => Plan();
        _harness.Bridge.Handlers[OperationNames.CableGenerateExecute] = args =>
        {
            File.WriteAllText(((CableGenerateArgs)args!).Backup!.Path, "copy");
            return _data;
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task DryRun_ShowsOnlyKnownScopeAndCounts_WithoutApprovalBackupOrExecution()
    {
        CallToolResult result = await Generate(dryRun: true);
        JsonElement data = WriteToolHarness.Structured(result);

        Assert.Equal((ExecutionState.NotStarted, true, 78, 0, 78), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("before").GetProperty("cables").GetInt32(), data.GetProperty("before").GetProperty("automatic").GetInt32(), data.GetProperty("before").GetProperty("automaticUnknown").GetInt32()));
        Assert.All(_dryUnknown, name => Assert.Equal(JsonValueKind.Null, data.GetProperty(name).ValueKind));
        Assert.Equal("Dry run: automatic cable generation only; new cables and selection results are known only after running. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Fact]
    public async Task ScratchWithNoAutomaticCables_StillApprovesBacksUpAndExecutesWithLongBudgetAndProgress()
    {
        _harness.Bridge.DuringWrite = progress => progress!.Report(new ProgressNotification { Progress = 1, Message = "Generating automatic cables" });
        JsonElement data = WriteToolHarness.Structured(await Generate());
        var sent = (CableGenerateArgs)_harness.Bridge.LastArgs!;
        ApprovalQuestion question = Assert.Single(_dialog.Questions);

        Assert.Equal(ToolTier.Destructive, question.Tier);
        Assert.Equal(("f1", "Standard", "Standard", "Standard", 150), (sent.Fingerprint, sent.CreationScheme, sent.NumberingScheme, sent.SelectionScheme, sent.Backup!.MaxSeconds));
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(_harness.Bridge.Writes).Limit);
        Assert.Contains(_progress.Values, p => p.Message == "Generating automatic cables");
        Assert.Equal((ExecutionState.Committed, 0, JsonValueKind.Null, sent.Backup.Path), (data.GetProperty("execution").GetString(), data.GetProperty("changedCount").GetInt32(), data.GetProperty("undo").ValueKind, data.GetProperty("backup").GetString()));
        Assert.True(File.Exists(sent.Backup.Path));
        Assert.Empty(data.GetProperty("eplanMessages").EnumerateArray());
        Assert.DoesNotContain("unavailable from the Bridge", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        JsonElement scope = data.GetProperty("scope");
        Assert.Equal((true, true, 1, 1), (scope.GetProperty("keepOldNames").GetBoolean(), scope.GetProperty("onlyAutoCables").GetBoolean(), scope.GetProperty("start").GetInt32(), scope.GetProperty("step").GetInt32()));
        Assert.Equal(ApprovalTexts.Change(Assert.Single(question.Plan.Changes), false, question.Plan.Details), Assert.Single(question.Changes));
    }

    [Fact]
    public async Task Approval_ShowsSpecTextCurrentCablesFixedParametersAndBackup_WithoutPromisingUndo()
    {
        await Generate();
        ApprovalAskArgs texts = ApprovalTexts.For(Assert.Single(_dialog.Questions), CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Generate or update automatic cables in this project using creation Standard, numbering Standard, selection Standard. Existing cable names will be preserved; existing cable renumbering is outside this operation. New cables and selection effects cannot be predicted in advance. A project backup will be made first. Undo after commit has not been verified.", texts.Changes[0]);
        Assert.Contains("KEEPOLDNAMES=1, ONLYAUTOCABLES=1, STARTVALUE=1, STEPVALUE=1", texts.Changes);
        Assert.Contains("+B1-WG2; automatic=null", texts.Changes);
        Assert.Contains(texts.Changes, line => line.StartsWith("Before: ", StringComparison.Ordinal));
        Assert.Contains("backed up", texts.Note, StringComparison.Ordinal);
        Assert.Contains("not been verified", texts.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("Ctrl+Z", texts.Note, StringComparison.Ordinal);
        string audit = string.Join("\n", Directory.EnumerateFiles(_harness.Locations.Audit).Select(File.ReadAllText));
        Assert.Contains("creationScheme", audit, StringComparison.Ordinal);
        Assert.Contains("selectionScheme", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownSchemes_PreserveCandidatesForAllThreeKinds_BeforeApproval()
    {
        string candidates = "{\"creationScheme\":[\"Standard\"],\"numberingScheme\":[\"Standard\"],\"selectionScheme\":[\"Standard\"]}";
        _harness.Bridge.Handlers[OperationNames.CableGeneratePlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "Choose explicit project schemes.",
            Metadata = new Dictionary<string, string> { ["violations"] = "Unknown schemes.", ["candidates"] = candidates },
        });

        JsonElement error = WriteToolHarness.Error(await Generate());

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(candidates, error.GetProperty("metadata").GetProperty("candidates").GetString());
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData("Denied", ErrorReason.ApprovalDenied)]
    [InlineData("Expired", ErrorReason.ApprovalExpired)]
    public async Task Refusal_DoesNotCreateBackupOrExecute(string decision, string reason)
    {
        _decision = Enum.Parse<ApprovalDecision>(decision);
        JsonElement error = WriteToolHarness.Error(await Generate());

        Assert.Equal((reason, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Theory]
    [InlineData(ErrorReason.BackupFailed, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.Cancelled, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.OutcomeUnknown, ExecutionState.Unknown)]
    public async Task ExecutionFailure_PreservesExecutionStateAndNeverResends(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Execution = execution, Message = "Generation failed." };
        JsonElement error = WriteToolHarness.Error(await Generate());

        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Single(_harness.Bridge.Writes);
        if (execution == ExecutionState.Unknown)
        {
            JsonElement again = WriteToolHarness.Error(await Generate());
            Assert.Equal(ErrorReason.WritesSuspended, again.GetProperty("reason").GetString());
            Assert.Single(_harness.Bridge.Writes);
        }
    }

    [Theory]
    [InlineData("renamed")]
    [InlineData("removed")]
    [InlineData("protected cable changed")]
    [InlineData("unknown automatic flag")]
    public async Task ReadbackGuardFailure_IsRolledBackWithDiagnostics(string change)
    {
        _harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = ErrorReason.EplanOperationFailed,
            Execution = ExecutionState.RolledBack,
            Message = "Check the schemes.",
            Metadata = new Dictionary<string, string> { ["change"] = change, ["before"] = "+B1-WG2" },
        };
        JsonElement error = WriteToolHarness.Error(await Generate());

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack, change), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("change").GetString()));
    }

    [Fact]
    public async Task RecoveredCommit_KeepsUnknownResultsNullAndBackupRetained()
    {
        _harness.Bridge.WriteRecovered = true;
        JsonElement data = WriteToolHarness.Structured(await Generate());

        Assert.Equal((ExecutionState.Committed, true), (data.GetProperty("execution").GetString(), data.GetProperty("recovered").GetBoolean()));
        Assert.All(_recoveredUnknown, name => Assert.Equal(JsonValueKind.Null, data.GetProperty(name).ValueKind));
        Assert.NotNull(data.GetProperty("backup").GetString());
        Assert.Contains("do not repeat", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changes_AreCleanedAndLimitedByCountAndBudget_WithoutLosingTheFullCount()
    {
        _data.ChangedCount = 250;
        _data.Changes = [.. Enumerable.Range(0, 250).Select(_ => new CableGenerateChange { Kind = "updated", Name = new string('文', 2000) + "\u0001\u202e" })];
        JsonElement data = WriteToolHarness.Structured(await Generate());

        Assert.Equal(250, data.GetProperty("changedCount").GetInt32());
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.InRange(data.GetProperty("changes").GetArrayLength(), 0, 200);
        Assert.DoesNotContain("\\u0001", data.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("\\u202E", data.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.InRange(ResultShaper.ResultTokens(data.GetRawText()), 1, ResultShaper.DefaultTokenBudget);
    }

    [Fact]
    public async Task Messages_MapAllFieldsAndCleanProjectText_WithoutUnavailableHint()
    {
        _data.EplanMessageCount = 1;
        _data.EplanMessages = [new SystemMessage { Level = "warning\u0001", Number = "S123456\u202e", Text = "Check\u0001 this\u202e scheme.", Occurrences = 3 }];

        JsonElement data = WriteToolHarness.Structured(await Generate());

        JsonElement message = Assert.Single(data.GetProperty("eplanMessages").EnumerateArray());
        Assert.Equal(("warning", "S123456", "Check this scheme.", 3), (message.GetProperty("level").GetString(), message.GetProperty("number").GetString(), message.GetProperty("text").GetString(), message.GetProperty("occurrences").GetInt32()));
        Assert.Contains("1 message", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("unavailable from the Bridge", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.False(data.GetProperty("truncated").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Messages_AreLimitedByCountAndBudget_KeepingFullMessageAndChangeCounts(bool longText)
    {
        _data.EplanMessageCount = 25;
        _data.EplanMessages = [.. Enumerable.Range(0, 25).Select(_ => new SystemMessage { Level = "error", Text = longText ? new string('文', 2000) : "Check scheme.", Occurrences = 1 })];
        _data.ChangedCount = 1;
        _data.Changes = [new CableGenerateChange { Kind = "created", Name = "+A1-W1" }];

        JsonElement data = WriteToolHarness.Structured(await Generate());

        Assert.Equal(1, data.GetProperty("changedCount").GetInt32());
        Assert.Single(data.GetProperty("changes").EnumerateArray());
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Contains("25 messages", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.InRange(data.GetProperty("eplanMessages").GetArrayLength(), 1, longText ? 19 : 20);
        Assert.InRange(ResultShaper.ResultTokens(data.GetRawText()), 1, ResultShaper.DefaultTokenBudget);
    }

    [Fact]
    public async Task MessageCollectionWarning_KeepsCommittedOutcomeAndBackup()
    {
        _data.Warning = "EPLAN messages could not be read; inspect the session messages in EPLAN.";

        JsonElement data = WriteToolHarness.Structured(await Generate());

        Assert.Equal(ExecutionState.Committed, data.GetProperty("execution").GetString());
        Assert.NotNull(data.GetProperty("backup").GetString());
        Assert.Contains("messages could not be read", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Empty(data.GetProperty("eplanMessages").EnumerateArray());
    }

    [Fact]
    public async Task FailedAction_PreservesBridgeMessageMetadataAndCount()
    {
        const string messages = "[{\"level\":\"error\",\"number\":\"S123456\",\"text\":\"Generation refused.\",\"occurrences\":1}]";
        _harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = ErrorReason.EplanOperationFailed,
            Execution = ExecutionState.RolledBack,
            Message = "Check the schemes.",
            Metadata = new Dictionary<string, string> { ["eplanMessages"] = messages, ["eplanMessageCount"] = "1" },
        };

        JsonElement error = WriteToolHarness.Error(await Generate());

        Assert.Equal(ExecutionState.RolledBack, error.GetProperty("execution").GetString());
        Assert.Equal(messages, error.GetProperty("metadata").GetProperty("eplanMessages").GetString());
        Assert.Equal("1", error.GetProperty("metadata").GetProperty("eplanMessageCount").GetString());
    }

    [Fact]
    public async Task BlankArguments_AreReportedTogetherBeforeBridgeAccess()
    {
        JsonElement error = WriteToolHarness.Error(await CableGenerateTools.CableGenerateAsync(_harness.Pipeline, _progress, " ", "", " ", "", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(4, error.GetProperty("metadata").GetProperty("violations").GetString()!.Split('\n').Length);
        Assert.Empty(_harness.Bridge.Calls);
    }

    private static CableGenerateCounts Counts() => new() { Cables = 78, AutomaticUnknown = 78, Connections = 12 };

    private WritePlan Plan()
    {
        WritePlan plan = _harness.Plan([new PlannedChange { Action = "update", Kind = "cables", Name = "Line1", Property = "generation" }]);
        plan.Details = new Dictionary<string, string>
        {
            ["creationScheme"] = "Standard",
            ["numberingScheme"] = "Standard",
            ["selectionScheme"] = "Standard",
            ["before"] = JsonSerializer.Serialize(Counts(), HostJson.Options),
            ["cables"] = "[{\"name\":\"+B1-WG2\",\"automatic\":null}]",
        };
        return plan;
    }

    private Task<CallToolResult> Generate(bool dryRun = false) => CableGenerateTools.CableGenerateAsync(_harness.Pipeline, _progress, "Line1", "Standard", "Standard", "Standard", dryRun, TestContext.Current.CancellationToken);
}
