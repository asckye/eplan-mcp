using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_language_set through the real write pipeline against a fake Bridge (docs/tools/eplan_language_set.md, sections
/// 3, 4, 6 and 7). The project has S23's shape: translation languages, no displayed languages (api-notes A28).
/// </summary>
[Trait("Size", "Medium")]
public sealed class LanguageSetToolTests : IDisposable
{
    private const string Restore = "Language settings are not undone by Ctrl+Z; call this tool again with the 'before' values to restore them.";

    private readonly FakeApprovalChannel _dialog;
    private readonly WriteToolHarness _harness;
    private List<PlannedChange> _changes = [Display("display: ##_##", "display: en_US, zh_CN")];
    private string _translationAfter = "de_DE\nen_US\nzh_CN";
    private string _displayAfter = "en_US\nzh_CN";
    private string _sourceBefore = "##_##";
    private string _sourceAfter = "##_##";

    public LanguageSetToolTests()
    {
        _dialog = new FakeApprovalChannel("eplan_dialog", null) { Answers = _ => ApprovalDecision.Granted };
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.LanguageSetPlan] = _ =>
        {
            WritePlan plan = _harness.Plan(_changes);
            plan.Details = new Dictionary<string, string>
            {
                ["translationBefore"] = "de_DE\nen_US\nzh_CN",
                ["displayBefore"] = "##_##",
                ["translationAfter"] = _translationAfter,
                ["displayAfter"] = _displayAfter,
                ["sourceBefore"] = _sourceBefore,
                ["sourceAfter"] = _sourceAfter,
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.LanguageSetExecute] = _ => new LanguageSetData
        {
            Project = "Line1",
            Before = new ProjectLanguages { Translation = ["de_DE", "en_US", "zh_CN"], Display = ["##_##"], Source = _sourceBefore },
            After = new ProjectLanguages { Translation = [.. _translationAfter.Split('\n')], Display = [.. _displayAfter.Split('\n')], Source = _sourceAfter },
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_ReturnsTheSettingsBeforeAndAfter_WithoutAskingOrChanging()
    {
        CallToolResult result = await Set(display: ["en_US", "zh_CN"], dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("undo").ValueKind));
        Assert.Equal(["##_##"], Codes(data.GetProperty("before").GetProperty("display")));
        Assert.Equal(["en_US", "zh_CN"], Codes(data.GetProperty("after").GetProperty("display")));
        Assert.Equal(["de_DE", "en_US", "zh_CN"], Codes(data.GetProperty("after").GetProperty("translation")));
        Assert.Equal("Dry run: would set the displayed languages to en_US, zh_CN. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Empty(_dialog.Questions);
    }

    [Fact]
    public async Task InScratch_TheLanguagesAreSetWithoutAsking_AndReadBack()
    {
        CallToolResult result = await Set(display: ["en_US", "zh_CN"]);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", false, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("recovered").GetBoolean(), data.GetProperty("undo").ValueKind));
        Assert.Equal(["en_US", "zh_CN"], Codes(data.GetProperty("after").GetProperty("display")));
        Assert.Equal(Restore, data.GetProperty("hint").GetString());
        Assert.Equal("Set the displayed languages to en_US, zh_CN.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        var sent = (LanguageSetArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("Line1", "f1", null), (sent.Project, sent.Fingerprint, sent.Add));
        Assert.Equal(["en_US", "zh_CN"], sent.Display!);
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task AddingAndShowingALanguage_NamesBothAndSaysTheLanguageStays()
    {
        _changes = [Display("translation: de_DE, en_US, zh_CN; display: ##_##", "translation: de_DE, en_US, zh_CN, sv_SE; display: sv_SE")];
        _translationAfter = "de_DE\nen_US\nzh_CN\nsv_SE";
        _displayAfter = "sv_SE";

        CallToolResult result = await Set(display: ["sv_SE"], add: ["sv_SE"]);

        Assert.Equal("Added sv_SE to the translation languages and set the displayed languages to sv_SE.", WriteToolHarness.Text(result, 0));
        Assert.Equal(Restore + " Languages added with 'add' are taken out only by eplan_language_remove.", WriteToolHarness.Structured(result).GetProperty("hint").GetString());
    }

    [Fact]
    public async Task ClearingTheDisplay_SaysSo()
    {
        _displayAfter = "##_##";

        Assert.Equal("Cleared the displayed languages.", WriteToolHarness.Text(await Set(display: ["##_##"]), 0));
    }

    [Fact]
    public async Task SettingsAlreadyAsAsked_ChangeNothingAndAskNobody()
    {
        _changes = [];
        _displayAfter = "##_##";

        CallToolResult result = await Set(add: ["en_US"]);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", false), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean()));
        Assert.Equal("The project already has these language settings.", data.GetProperty("hint").GetString());
        Assert.Equal("Nothing to change: the project already has these languages.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task DisplayedLanguagesEplanChangedByItself_AreNamedInTheHint()
    {
        _changes = [Display("translation: de_DE, en_US, zh_CN", "translation: de_DE, en_US, zh_CN, sv_SE")];
        _translationAfter = "de_DE\nen_US\nzh_CN\nsv_SE";
        _displayAfter = "zh_CN";

        JsonElement data = WriteToolHarness.Structured(await Set(add: ["sv_SE"]));

        Assert.Contains("EPLAN also changed the displayed languages to zh_CN; give 'display' to set them.", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecoveredCommit_IsAsPlannedAndSaysHowToReadTheSettings()
    {
        _harness.Bridge.WriteRecovered = true;

        CallToolResult result = await Set(display: ["en_US", "zh_CN"]);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", true), (data.GetProperty("execution").GetString(), data.GetProperty("recovered").GetBoolean()));
        Assert.Equal(["en_US", "zh_CN"], Codes(data.GetProperty("after").GetProperty("display")));
        Assert.Contains("call this tool with dry_run to read the settings now", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutsideScratch_TheEngineerIsAskedWithOneLine()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        WriteToolHarness.Structured(await Set(display: ["en_US", "zh_CN"]));

        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal(["update project Line1: languages 'display: ##_##' -> 'display: en_US, zh_CN'"], question.Changes);
    }

    [Theory]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.OutcomeUnknown, ExecutionState.Unknown)]
    public async Task ABridgeError_KeepsItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = reason,
            Message = "m",
            Execution = execution,
            Metadata = new Dictionary<string, string> { ["translation"] = "de_DE\nen_US\nzh_CN", ["display"] = "##_##" },
        };

        JsonElement error = WriteToolHarness.Error(await Set(display: ["en_US"]));

        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal("##_##", error.GetProperty("metadata").GetProperty("display").GetString());
    }

    [Fact]
    public async Task AnInvalidLanguage_FromThePlan_IsNotStartedWithTheCandidates()
    {
        _harness.Bridge.Handlers[OperationNames.LanguageSetPlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "Invalid arguments: display[0] is not a translation language of the project; add it with 'add', or use one of metadata.candidates.",
            Metadata = new Dictionary<string, string> { ["violations"] = "display[0] is not a translation language of the project; add it with 'add', or use one of metadata.candidates.", ["candidates"] = "de_DE\nen_US" },
        });

        JsonElement error = WriteToolHarness.Error(await Set(display: ["fr_FR"]));

        Assert.Equal(("INVALID_ARGUMENT", "not_started", "de_DE\nen_US"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("candidates").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(" ", new[] { "en_US" }, null, "'project' is required")]
    [InlineData("Line1", null, null, "Give at least one of 'display', 'add' and 'source'.")]
    [InlineData("Line1", new string[0], null, "'display' must hold 1 to 10 language codes such as en_US; omit it to leave it out.")]
    [InlineData("Line1", null, new[] { "aa_AA", "ab_AA", "ac_AA", "ad_AA", "ae_AA", "af_AA", "ag_AA", "ah_AA", "ai_AA", "aj_AA", "ak_AA" }, "'add' must hold 1 to 10 language codes such as en_US; omit it to leave it out.")]
    public async Task HostChecks_FailBeforeTheBridgeIsAsked(string project, string[]? display, string[]? add, string violation)
    {
        JsonElement error = WriteToolHarness.Error(await LanguageWriteTools.LanguageSetAsync(_harness.Pipeline, new RecordedProgress(), project, display, add, null, false, TestContext.Current.CancellationToken));

        Assert.Contains(violation, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    [Fact]
    public async Task AnEmptySource_IsLikeAnOmittedOne()
    {
        JsonElement error = WriteToolHarness.Error(await Set(source: string.Empty));

        Assert.Contains("Give at least one of 'display', 'add' and 'source'.", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingTheSource_IsSentReadBackAndNamed()
    {
        _changes = [Display("source: ##_##", "source: de_DE")];
        _displayAfter = "##_##";
        _sourceAfter = "de_DE";

        CallToolResult dryRun = await Set(source: "de_DE", dryRun: true);
        CallToolResult result = await Set(source: "de_DE");

        Assert.Equal("Dry run: would set the source language to de_DE. Nothing was changed.", WriteToolHarness.Text(dryRun, 0));
        Assert.Equal(("##_##", "de_DE"), (WriteToolHarness.Structured(dryRun).GetProperty("before").GetProperty("source").GetString(), WriteToolHarness.Structured(dryRun).GetProperty("after").GetProperty("source").GetString()));
        Assert.Equal("Set the source language to de_DE.", WriteToolHarness.Text(result, 0));
        Assert.Equal("de_DE", WriteToolHarness.Structured(result).GetProperty("after").GetProperty("source").GetString());
        var sent = (LanguageSetArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("de_DE", "f1", null, null), (sent.Source, sent.Fingerprint, sent.Display, sent.Add));
    }

    [Fact]
    public async Task AddingClearingTheSourceAndShowing_InADryRun_IsWordedInThePresent()
    {
        _changes = [Display("translation: de_DE, en_US, zh_CN; source: de_DE; display: ##_##", "translation: de_DE, en_US, zh_CN, sv_SE; source: ##_##; display: ##_##")];
        _translationAfter = "de_DE\nen_US\nzh_CN\nsv_SE";
        _sourceBefore = "de_DE";
        _displayAfter = "##_##";

        CallToolResult result = await Set(display: ["##_##"], add: ["sv_SE"], source: "##_##", dryRun: true);

        Assert.Equal("Dry run: would add sv_SE to the translation languages, clear the source language and clear the displayed languages. Nothing was changed.", WriteToolHarness.Text(result, 0));
    }

    private static PlannedChange Display(string before, string after) =>
        new() { Action = "update", Kind = "project", Name = "Line1", Property = "languages", OldValue = before, NewValue = after };

    private static List<string?> Codes(JsonElement list) => [.. list.EnumerateArray().Select(code => code.GetString())];

    private Task<CallToolResult> Set(string[]? display = null, string[]? add = null, string? source = null, bool dryRun = false) =>
        LanguageWriteTools.LanguageSetAsync(_harness.Pipeline, new RecordedProgress(), "Line1", display, add, source, dryRun, TestContext.Current.CancellationToken);
}
