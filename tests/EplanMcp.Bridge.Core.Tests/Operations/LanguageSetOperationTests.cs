using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// language.set.plan and language.set.execute against a fake project (docs/tools/eplan_language_set.md, sections 2 to 7).
/// The project has three translation languages and no displayed languages, the shape of S23's sample project; its
/// settings are not taken back by a rollback (api-notes A28), so every failure writes them back.
/// </summary>
[Trait("Size", "Small")]
public sealed class LanguageSetOperationTests
{
    private const string Before = "de_DE;en_US;zh_CN;";

    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakeProjectLanguages _languages = new();
    private readonly FakeTransactions _transactions = new();

    public LanguageSetOperationTests() => _catalog = new FakeCatalog(_line1);

    [Fact]
    public void Plan_Display_IsOneUpdateOfTheProjectsLanguagesWithBothListsInTheDetails()
    {
        WritePlan plan = Plan(Args(display: ["en_US", "zh_CN"]));

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "project", "Line1", "languages", "display: ##_##", "display: en_US, zh_CN"), (change.Action, change.Kind, change.Name, change.Property, change.OldValue, change.NewValue));
        Assert.Equal((_line1.Name, _line1.Path), (plan.Project, plan.ProjectPath));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["translationBefore"] = "de_DE\nen_US\nzh_CN",
                ["displayBefore"] = "##_##",
                ["translationAfter"] = "de_DE\nen_US\nzh_CN",
                ["displayAfter"] = "en_US\nzh_CN",
            },
            plan.Details);
        Assert.Empty(_languages.Calls);
    }

    [Fact]
    public void Plan_AddAndDisplay_NamesBothSettingsAndSkipsLanguagesTheProjectHas()
    {
        WritePlan plan = Plan(Args(display: ["sv_SE"], add: ["en_US", "sv_SE"]));

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal("translation: de_DE, en_US, zh_CN; display: ##_##", change.OldValue);
        Assert.Equal("translation: de_DE, en_US, zh_CN, sv_SE; display: sv_SE", change.NewValue);
        Assert.Equal("de_DE\nen_US\nzh_CN\nsv_SE", plan.Details!["translationAfter"]);
    }

    [Fact]
    public void Plan_SettingsAlreadyAsAsked_HasNoChange()
    {
        _languages.Settings.Display = "en_US;zh_CN;";

        WritePlan plan = Plan(Args(display: ["en_US", "zh_CN"], add: ["de_DE"]));

        Assert.Empty(plan.Changes);
        Assert.Equal("en_US\nzh_CN", plan.Details!["displayAfter"]);
    }

    [Fact]
    public void Plan_TheSameLanguagesInAnotherOrder_AreAChange()
    {
        _languages.Settings.Display = "en_US;zh_CN;";

        PlannedChange change = Assert.Single(Plan(Args(display: ["zh_CN", "en_US"])).Changes);

        Assert.Equal(("display: en_US, zh_CN", "display: zh_CN, en_US"), (change.OldValue, change.NewValue));
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        var args = new LanguageSetArgs { Display = ["en_us", "zh_CN", "zh_CN", "##_##"], Add = [] };

        string[] violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"].Split('\n');

        Assert.Equal(
            [
                "'project' is required for writes: a name or path from eplan_project_list.",
                "display[0] is not a language code of the form xx_YY, such as en_US or zh_CN.",
                "display[2] repeats display[1].",
                "display[3] is ##_##, which clears the setting and must be the only entry.",
                "'add' must hold 1 to 10 language codes such as en_US; omit it to leave it out.",
            ],
            violations);
    }

    [Fact]
    public void Plan_NeitherDisplayNorAdd_IsInvalid()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new LanguageSetArgs { Project = "Line1" })).Error;

        Assert.Equal((ErrorReason.InvalidArgument, "Give 'display', 'add' or both."), (error.Reason, error.Metadata!["violations"]));
    }

    [Fact]
    public void Plan_ADisplayedLanguageTheProjectDoesNotHave_IsInvalidWithTheTranslationLanguagesAsCandidates()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(display: ["en_US", "fr_FR", "sv_SE"], add: ["sv_SE"]))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal("display[1] is not a translation language of the project; add it with 'add', or use one of metadata.candidates.", error.Metadata!["violations"]);
        Assert.Equal("de_DE\nen_US\nzh_CN", error.Metadata["candidates"]);
    }

    [Fact]
    public void Execute_Display_SetsTheLanguagesSeparatedByCommasAndReadsThemBack()
    {
        LanguageSetData data = Execute(Args(display: ["en_US", "zh_CN"]));

        Assert.Equal(["SetDisplayed en_US,zh_CN"], _languages.Calls);
        Assert.Equal("en_US;zh_CN;", _languages.Settings.Display);
        Assert.Equal(("Line1", null), (data.Project, data.Warning));
        Assert.Equal(["de_DE", "en_US", "zh_CN"], data.Before.Translation);
        Assert.Equal(["##_##"], data.Before.Display);
        Assert.Equal(["de_DE", "en_US", "zh_CN"], data.After.Translation);
        Assert.Equal(["en_US", "zh_CN"], data.After.Display);
        Assert.Equal(["Begin AI: eplan_language_set", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_Add_AddsOnlyTheMissingLanguagesThenSetsTheDisplay()
    {
        LanguageSetData data = Execute(Args(display: ["sv_SE", "en_US"], add: ["en_US", "sv_SE", "fi_FI"]));

        Assert.Equal(["Add sv_SE", "Add fi_FI", "SetDisplayed sv_SE,en_US"], _languages.Calls);
        Assert.Equal(["de_DE", "en_US", "zh_CN", "sv_SE", "fi_FI"], data.After.Translation);
        Assert.Equal(["sv_SE", "en_US"], data.After.Display);
    }

    [Fact]
    public void Execute_AddOnly_LeavesTheDisplayAloneAndDoesNotCompareIt()
    {
        _languages.OnChange = _ => _languages.Settings.Display = "zh_CN;"; // as if EPLAN changed it itself

        LanguageSetData data = Execute(Args(add: ["sv_SE"]));

        Assert.Equal(["Add sv_SE"], _languages.Calls);
        Assert.Equal(["zh_CN"], data.After.Display);
    }

    [Fact]
    public void Execute_Unset_WritesTheStoredUnsetBecauseEplanRefusesIt()
    {
        _languages.Settings.Display = "en_US;zh_CN;";

        LanguageSetData data = Execute(Args(display: ["##_##"]));

        Assert.Equal(["Write " + Before + " ##_##;"], _languages.Calls);
        Assert.Equal(["##_##"], data.After.Display);
    }

    [Fact]
    public void Execute_AReadBackOtherThanAsked_IsRolledBackAndTheSettingsWrittenBack()
    {
        _languages.KeepDisplay = true;
        _languages.OnChange = call => _languages.Settings.Display = "zh_CN;";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args(display: ["en_US", "zh_CN"], add: ["sv_SE"]))).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal("displayed languages: asked for en_US, zh_CN, EPLAN has zh_CN", error.Metadata!["problem"]);
        Assert.Equal(["Add sv_SE", "SetDisplayed en_US,zh_CN", "Write " + Before + " ##_##;"], _languages.Calls);
        Assert.Equal((Before, "##_##;"), (_languages.Settings.Translation, _languages.Settings.Display));
        Assert.Equal(["Begin AI: eplan_language_set", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_WhenEplanRefusesAfterAnAdd_TheAddedLanguageIsWrittenBackToo()
    {
        _languages.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused the displayed languages.");
        _languages.FailOn = "SetDisplayed";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args(display: ["sv_SE"], add: ["sv_SE"]))).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(Before, _languages.Settings.Translation);
        Assert.Equal("Write " + Before + " ##_##;", _languages.Calls[^1]);
    }

    [Fact]
    public void Execute_AFailureBeforeAnyChange_WritesNothingBack()
    {
        _languages.Failure = new InvalidOperationException("EPLAN failed");

        Assert.Throws<InvalidOperationException>(() => Execute(Args(display: ["en_US"])));

        Assert.Equal(["SetDisplayed en_US"], _languages.Calls);
    }

    [Fact]
    public void Execute_AWriteBackThatFails_IsOutcomeUnknownWithTheSettingsAsTheyWere()
    {
        _languages.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused the displayed languages.");
        _languages.FailOn = "SetDisplayed";
        _languages.WriteFailure = new InvalidOperationException("the setting cannot be written");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args(display: ["sv_SE"], add: ["sv_SE"]))).Error;

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        Assert.Contains("Do not call this tool again. Ask the engineer to set the languages back in EPLAN's project settings (Translation).", error.Message, StringComparison.Ordinal);
        Assert.Equal(("de_DE\nen_US\nzh_CN", "##_##"), (error.Metadata!["translation"], error.Metadata["display"]));
    }

    [Fact]
    public void Execute_AfterTheSettingsChangedMeanwhile_IsPlanStaleAndChangesNothing()
    {
        LanguageSetArgs args = Args(display: ["en_US"]);
        args.Fingerprint = Plan(args).Fingerprint;
        _languages.Settings.Display = "zh_CN;"; // the engineer, meanwhile

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_languages.Calls);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutAFingerprint_IsNotStarted()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(Args(display: ["en_US"]))).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Fact]
    public void ACancelDuringTheChange_RollsBackAndWritesTheSettingsBack()
    {
        LanguageSetArgs args = Args(display: ["en_US"], add: ["sv_SE"]);
        args.Fingerprint = Plan(args).Fingerprint;
        var router = new OperationRouter(
            [new LanguageSetExecuteOperation(_catalog, _languages, _transactions)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));
        _languages.OnChange = call => router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "r1" }, "cancel");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.LanguageSetExecute, JObject.FromObject(args, BridgeJson.Serializer), "r1")).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(["Add sv_SE", "Write " + Before + " ##_##;"], _languages.Calls);
        Assert.Equal(Before, _languages.Settings.Translation);
    }

    private static LanguageSetArgs Args(List<string>? display = null, List<string>? add = null) => new() { Project = "Line1", Display = display, Add = add };

    private WritePlan Plan(LanguageSetArgs args) => (WritePlan)new LanguageSetPlanOperation(_catalog, _languages).Execute(args)!;

    /// <summary>Plans, then executes with the plan's fingerprint, as the Host does.</summary>
    private LanguageSetData Execute(LanguageSetArgs args)
    {
        args.Fingerprint = Plan(args).Fingerprint;
        return Run(args);
    }

    private LanguageSetData Run(LanguageSetArgs args) =>
        (LanguageSetData)new LanguageSetExecuteOperation(_catalog, _languages, _transactions).Execute(args)!;
}
