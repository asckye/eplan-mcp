using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_project_translate through the real write pipeline against a fake Bridge (docs/tools/eplan_project_translate.md,
/// sections 3, 4, 6 and 7): the source language not set, nothing to translate into, missing pages, a read-back that
/// differs, the approval and the backup; S23 r2 translated the German "Motor" into 19 languages (api-notes A28).
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectTranslateToolTests : IDisposable
{
    private const string Scope = "the project: 37 of 497 texts miss a translation language; from de_DE into 2 languages";

    private readonly FakeApprovalChannel _dialog = new("eplan_dialog", ApprovalDecision.Granted);
    private readonly WriteToolHarness _harness;
    private int _missing = 37;
    private ProjectTranslateData _data = new()
    {
        Project = "Line1",
        Source = "de_DE",
        Languages = ["de_DE", "en_US", "zh_CN"],
        TextsChecked = 497,
        TextsChanged = 37,
        Changed = [new TranslatedText { Kind = "function", Name = "=GAA+A1-WE1", Property = "FUNC_TEXT", MissingBefore = ["en_US", "zh_CN"], MissingAfter = [] }],
        StillMissing = 5,
    };

    public ProjectTranslateToolTests()
    {
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.ProjectTranslatePlan] = _ =>
        {
            WritePlan plan = _harness.Plan(_missing == 0 ? [] : [new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "translations", OldValue = Scope }]);
            plan.Details = new Dictionary<string, string>
            {
                ["source"] = "de_DE",
                ["languages"] = "de_DE\nen_US\nzh_CN",
                ["textsChecked"] = "497",
                ["textsMissing"] = _missing.ToString(CultureInfo.InvariantCulture),
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ProjectTranslateExecute] = args =>
        {
            File.WriteAllText(((ProjectTranslateArgs)args!).Backup!.Path, "copy");
            return _data;
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void TheTool_IsDestructive_AndOnlyInTheFullProfile()
    {
        Assert.DoesNotContain(ProjectDestructiveTools.TranslateTool, ToolProfiles.Core);
    }

    [Fact]
    public async Task ADryRun_ShowsTheScopeOnly_WithoutAskingOrBackingUp()
    {
        CallToolResult result = await Translate(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, "de_DE", 497, 37), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("source").GetString(), data.GetProperty("textsChecked").GetInt32(), data.GetProperty("textsMissing").GetInt32()));
        Assert.Equal(3, data.GetProperty("languages").GetArrayLength());
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("changed").ValueKind, data.GetProperty("stillMissing").ValueKind, data.GetProperty("backup").ValueKind));
        Assert.Equal("Dry run: would translate from de_DE into the project's 3 languages; 37 of 497 texts miss a translation language. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task InScratch_TheEngineerIsAsked_ThenEplanTranslatesAfterABackup()
    {
        CallToolResult result = await Translate(pages: ["=GAA+A1&EFS1/1"]);

        JsonElement data = WriteToolHarness.Structured(result);
        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal(["translate " + Scope], question.Changes);
        var sent = (ProjectTranslateArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("f1", 150), (sent.Fingerprint, sent.Backup!.MaxSeconds));
        Assert.Equal(["=GAA+A1&EFS1/1"], sent.Pages!);
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(_harness.Bridge.Writes).Limit);
        Assert.Equal(("committed", 37, 5, "AI: eplan_project_translate", sent.Backup.Path), (data.GetProperty("execution").GetString(), data.GetProperty("textsChanged").GetInt32(), data.GetProperty("stillMissing").GetInt32(), data.GetProperty("undo").GetString(), data.GetProperty("backup").GetString()));
        JsonElement changed = Assert.Single(data.GetProperty("changed").EnumerateArray());
        Assert.Equal(("function", "=GAA+A1-WE1", "FUNC_TEXT", 2, 0), (changed.GetProperty("kind").GetString(), changed.GetProperty("name").GetString(), changed.GetProperty("property").GetString(), changed.GetProperty("missingBefore").GetArrayLength(), changed.GetProperty("missingAfter").GetArrayLength()));
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Contains("export them with eplan_export_untranslated", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Translated 37 texts into the project's 3 languages; 5 texts still miss a language because the translation database has no entry for them. A backup was made first (see 'backup').", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public void TheApprovalDialog_NamesTheScopeAndThatTranslationsMayBeRewritten()
    {
        WritePlan plan = _harness.Plan([new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "translations", OldValue = Scope }]);

        var question = new ApprovalQuestion("r", ProjectDestructiveTools.TranslateTool, "h", plan, [], DateTimeOffset.UnixEpoch, BackupFolder: @"D:\Backups") { Tier = ToolTier.Destructive, Eplan = null };
        ApprovalAskArgs en = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US"));
        ApprovalAskArgs zh = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal("Translate with the translation database: " + Scope, en.Changes[0]);
        Assert.Contains("existing translations may be rewritten", en.Changes[^1], StringComparison.Ordinal);
        Assert.Equal("按翻译库翻译：" + Scope, zh.Changes[0]);
        Assert.Contains("已有的译文也可能按翻译库改写", zh.Changes[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingMissing_IsNotStartedAndAsksNobody()
    {
        _missing = 0;

        CallToolResult result = await Translate();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal("not_started", data.GetProperty("execution").GetString());
        Assert.StartsWith("Every text with a text in the source language has all the translation languages", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Nothing to translate: no text in the source language misses a translation language. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task EplanChangingNoText_IsNotStartedWithTheBackupAndNothingToUndo()
    {
        _data = new ProjectTranslateData { Project = "Line1", Source = "de_DE", Languages = ["de_DE", "en_US"], TextsChecked = 497, StillMissing = 37 };

        CallToolResult result = await Translate();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", JsonValueKind.Null, JsonValueKind.String), (data.GetProperty("execution").GetString(), data.GetProperty("undo").ValueKind, data.GetProperty("backup").ValueKind));
        Assert.Equal("EPLAN changed no text, so nothing was changed. A backup was made first (see 'backup').", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task ARecoveredCommit_SaysToCheckInEplan()
    {
        _harness.Bridge.WriteRecovered = true;

        JsonElement data = WriteToolHarness.Structured(await Translate());

        Assert.Equal(("committed", true, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("recovered").GetBoolean(), data.GetProperty("changed").ValueKind));
        Assert.Contains("ask the engineer to check the texts in EPLAN", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ErrorReason.InvalidArgument, "The project's source language is not set (##_##)")]
    [InlineData(ErrorReason.PageNotFound, "No page has these names")]
    public async Task APlanError_IsNotStartedAndAsksNobody(string reason, string message)
    {
        _harness.Bridge.Handlers[OperationNames.ProjectTranslatePlan] = _ => throw new BridgeCallException(new BridgeError { Reason = reason, Message = message });

        JsonElement error = WriteToolHarness.Error(await Translate());

        Assert.Equal((reason, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task AReadBackThatDiffers_IsRolledBack()
    {
        _harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = ErrorReason.EplanOperationFailed,
            Message = "EPLAN changed texts in the source language while translating, so the translation was rolled back.",
            Execution = ExecutionState.RolledBack,
            Metadata = new Dictionary<string, string> { ["problem"] = "1 texts changed in the source language de_DE: =GAA+A1-FC1" },
        };

        JsonElement error = WriteToolHarness.Error(await Translate());

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    [Fact]
    public async Task HostChecks_FailBeforeTheBridgeIsAsked()
    {
        JsonElement error = WriteToolHarness.Error(await ProjectDestructiveTools.ProjectTranslateAsync(_harness.Pipeline, new RecordedProgress(), " ", [], false, TestContext.Current.CancellationToken));

        Assert.Equal(
            "'project' is required for writes: a name or path from eplan_project_list.\n'pages' must hold 1 to 200 full page names; omit it for the whole project.",
            error.GetProperty("metadata").GetProperty("violations").GetString());
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    private Task<CallToolResult> Translate(string[]? pages = null, bool dryRun = false) =>
        ProjectDestructiveTools.ProjectTranslateAsync(_harness.Pipeline, new RecordedProgress(), "Line1", pages, dryRun, TestContext.Current.CancellationToken);
}
