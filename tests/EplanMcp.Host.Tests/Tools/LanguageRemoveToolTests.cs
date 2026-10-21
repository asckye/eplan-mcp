using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_language_remove through the real write pipeline against a fake Bridge (docs/tools/eplan_language_remove.md,
/// sections 3, 4, 6 and 7): asked in scratch too, backed up before it runs, and S23's removal of sv_SE with its 460
/// translations, after which EPLAN showed zh_CN (api-notes A28).
/// </summary>
[Trait("Size", "Medium")]
public sealed class LanguageRemoveToolTests : IDisposable
{
    private readonly FakeApprovalChannel _dialog;
    private readonly WriteToolHarness _harness;
    private ApprovalDecision _decision = ApprovalDecision.Granted;
    private string _displayAfter = "zh_CN";

    public LanguageRemoveToolTests()
    {
        _dialog = new FakeApprovalChannel("eplan_dialog", null) { Answers = _ => _decision };
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.LanguageRemovePlan] = args =>
        {
            List<string> languages = ((LanguageRemoveArgs)args!).Languages;
            WritePlan plan = _harness.Plan(languages.Select(code => new PlannedChange { Action = "delete", Kind = "language", Name = code, OldValue = "460 translations" }));
            plan.Details = new Dictionary<string, string>
            {
                ["textsAffected"] = languages.Count == 1 ? "460" : "470",
                ["translation"] = "de_DE\nen_US\nfi_FI\nsv_SE\nzh_CN",
                ["display"] = "##_##",
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.LanguageRemoveExecute] = args =>
        {
            var remove = (LanguageRemoveArgs)args!;
            File.WriteAllText(remove.Backup!.Path, "copy");
            return new LanguageRemoveData
            {
                Project = "Line1",
                Removed = remove.Languages,
                TextsAffected = remove.Languages.Count == 1 ? 460 : 470,
                Before = new ProjectLanguages { Translation = ["de_DE", "en_US", "fi_FI", "sv_SE", "zh_CN"], Display = ["##_##"] },
                After = new ProjectLanguages { Translation = ["de_DE", "en_US", "fi_FI", "zh_CN"], Display = [_displayAfter] },
            };
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_CountsTheTranslations_WithoutAskingOrBackingUp()
    {
        CallToolResult result = await Remove(["sv_SE"], dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, 460), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("textsAffected").GetInt32()));
        Assert.All([data.GetProperty("after"), data.GetProperty("backup"), data.GetProperty("undo"), data.GetProperty("hint")], v => Assert.Equal(JsonValueKind.Null, v.ValueKind));
        Assert.Equal(["sv_SE"], data.GetProperty("removed").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(5, data.GetProperty("before").GetProperty("translation").GetArrayLength());
        Assert.Equal("Dry run: would remove sv_SE and its 460 translations. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Fact]
    public async Task InScratch_TheEngineerIsAsked_ThenTheProjectIsBackedUpAndTheLanguageRemoved()
    {
        CallToolResult result = await Remove(["sv_SE"]);

        JsonElement data = WriteToolHarness.Structured(result);
        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal(["delete language sv_SE (460 translations)"], question.Changes);
        var sent = (LanguageRemoveArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("f1", "sv_SE"), (sent.Fingerprint, Assert.Single(sent.Languages)));
        Assert.StartsWith(Path.Combine(_harness.Locations.Backups, "Line1") + Path.DirectorySeparatorChar, sent.Backup!.Path, StringComparison.Ordinal);
        Assert.Equal(150, sent.Backup.MaxSeconds);
        Assert.Equal(("committed", sent.Backup.Path, "AI: eplan_language_remove"), (data.GetProperty("execution").GetString(), data.GetProperty("backup").GetString(), data.GetProperty("undo").GetString()));
        Assert.Equal(["zh_CN"], data.GetProperty("after").GetProperty("display").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(
            "EPLAN also changed the displayed languages to zh_CN; use eplan_language_set to change them back. Ctrl+Z in EPLAN may bring the translations back without the language settings; the backup has both.",
            data.GetProperty("hint").GetString());
        Assert.Equal("Removed sv_SE and its 460 translations. A backup was made first (see 'backup').", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task TwoLanguages_CountTheTextsOnce_AndNoDisplayHintWhenEplanKeptThem()
    {
        _displayAfter = "##_##";

        CallToolResult result = await Remove(["sv_SE", "fi_FI"]);

        Assert.Equal("Removed sv_SE, fi_FI and their translations in 470 texts. A backup was made first (see 'backup').", WriteToolHarness.Text(result, 0));
        Assert.DoesNotContain("displayed languages", WriteToolHarness.Structured(result).GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_NothingIsBackedUpOrRemoved()
    {
        _decision = ApprovalDecision.Denied;

        JsonElement error = WriteToolHarness.Error(await Remove(["sv_SE"]));

        Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(Directory.Exists(_harness.Locations.Backups));
    }

    [Fact]
    public async Task ARecoveredCommit_HasNoAfterAndSaysHowToReadTheSettings()
    {
        _harness.Bridge.WriteRecovered = true;
        _harness.Bridge.Handlers[OperationNames.LanguageRemoveExecute] = args =>
        {
            File.WriteAllText(((LanguageRemoveArgs)args!).Backup!.Path, "copy");
            return new LanguageRemoveData();
        };

        JsonElement data = WriteToolHarness.Structured(await Remove(["sv_SE"]));

        Assert.Equal(("committed", true, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("recovered").GetBoolean(), data.GetProperty("after").ValueKind));
        Assert.Contains("eplan_language_set with dry_run reads the settings now", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ErrorReason.BackupFailed, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.OutcomeUnknown, ExecutionState.Unknown)]
    public async Task ABridgeError_KeepsItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Message = "m", Execution = execution };

        JsonElement error = WriteToolHarness.Error(await Remove(["sv_SE"]));

        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        if (execution != ExecutionState.NotStarted)
        {
            // The pipeline names the backup of a write that ran, so the engineer can restore from it (ADR-0013).
            Assert.StartsWith(_harness.Locations.Backups, error.GetProperty("metadata").GetProperty("backup").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheSourceLanguage_FromThePlan_IsNotStartedAndAsksNobody()
    {
        _harness.Bridge.Handlers[OperationNames.LanguageRemovePlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "Invalid arguments: languages[0] is the project's source language, which EPLAN cannot remove.",
            Metadata = new Dictionary<string, string> { ["violations"] = "languages[0] is the project's source language, which EPLAN cannot remove.", ["candidates"] = "de_DE\nsv_SE" },
        });

        JsonElement error = WriteToolHarness.Error(await Remove(["de_DE"]));

        Assert.Equal(("INVALID_ARGUMENT", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_dialog.Questions);
    }

    [Theory]
    [InlineData(" ", 1, "'project' is required")]
    [InlineData("Line1", 0, "'languages' must hold 1 to 10 language codes such as en_US.")]
    [InlineData("Line1", 11, "'languages' must hold 1 to 10 language codes such as en_US.")]
    public async Task HostChecks_FailBeforeTheBridgeIsAsked(string project, int count, string violation)
    {
        string[] languages = [.. Enumerable.Range(0, count).Select(i => "a" + (char)('a' + i) + "_AA")];

        JsonElement error = WriteToolHarness.Error(await LanguageDestructiveTools.LanguageRemoveAsync(_harness.Pipeline, new RecordedProgress(), project, languages, false, TestContext.Current.CancellationToken));

        Assert.Contains(violation, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    private Task<CallToolResult> Remove(string[] languages, bool dryRun = false) =>
        LanguageDestructiveTools.LanguageRemoveAsync(_harness.Pipeline, new RecordedProgress(), "Line1", languages, dryRun, TestContext.Current.CancellationToken);
}
