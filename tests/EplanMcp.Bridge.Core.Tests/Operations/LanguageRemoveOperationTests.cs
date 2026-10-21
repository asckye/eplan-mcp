using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// language.remove.plan and language.remove.execute against a fake project (docs/tools/eplan_language_remove.md, sections
/// 2 to 7), shaped like S23's sample project (api-notes A28): removing sv_SE deletes its translations, takes it out of
/// the translation languages and changes the displayed languages to zh_CN; a rollback restores the texts but not the
/// settings, so every failure writes them back. Medium: the backup goes to a real temporary folder.
/// </summary>
[Trait("Size", "Medium")]
public sealed class LanguageRemoveOperationTests : IDisposable
{
    private const string Before = "de_DE;en_US;fi_FI;sv_SE;zh_CN;";

    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-language-").FullName;
    private readonly FakePageReader _pages = new();
    private readonly FakeProjectLanguages _languages = new();
    private readonly FakeLanguageRemover _remover;
    private readonly FakeProjectBackup _backup;
    private readonly FakeTransactions _transactions = new();

    public LanguageRemoveOperationTests()
    {
        _pages.Pages.AddRange([new PageSummary { Name = "=A/1" }, new PageSummary { Name = "=A/2" }]);
        _backup = new FakeProjectBackup(_pages);
        _languages.Settings.Translation = Before;
        _remover = new FakeLanguageRemover(_languages);
        _remover.Stored.AddRange(
        [
            new LanguageText { Id = "F 1", Raw = "de_DE@Motor;en_US@Motor;sv_SE@Motor;zh_CN@电机;" },
            new LanguageText { Id = "F 2", Raw = "de_DE@Schütz;sv_SE@Kontaktor;fi_FI@Kontaktori;" },
            new LanguageText { Id = "P 1", Raw = "de_DE@Einspeisung;fi_FI@Syöttö;" },
            new LanguageText { Id = "P 2", Raw = "??_??@=A1;" },
        ]);
        _transactions.Snapshot = _remover.Snapshot;
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Plan_IsOneDeletePerLanguageWithItsTranslations_AndTheTextsAffectedOnce()
    {
        WritePlan plan = Plan(Args("sv_SE", "fi_FI"));

        Assert.Equal(
            [("delete", "language", "sv_SE", "2 translations"), ("delete", "language", "fi_FI", "2 translations")],
            plan.Changes.Select(c => (c.Action, c.Kind, c.Name, c.OldValue)));
        Assert.Equal(
            new Dictionary<string, string> { ["textsAffected"] = "3", ["translation"] = "de_DE\nen_US\nfi_FI\nsv_SE\nzh_CN", ["display"] = "##_##" },
            plan.Details);
        Assert.Empty(_remover.Removals);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        var args = new LanguageRemoveArgs { Languages = ["sv_se", "fi_FI", "fi_FI"] };

        string[] violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"].Split('\n');

        Assert.Equal(
            [
                "'project' is required for writes: a name or path from eplan_project_list.",
                "languages[0] is not a language code of the form xx_YY, such as en_US or zh_CN.",
                "languages[2] repeats languages[1].",
            ],
            violations);
    }

    [Fact]
    public void Plan_NoLanguages_IsInvalid()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new LanguageRemoveArgs { Project = "Line1" })).Error;

        Assert.Equal("'languages' must hold 1 to 10 language codes such as en_US.", error.Metadata!["violations"]);
    }

    [Fact]
    public void Plan_TheSourceLanguageOrOneTheProjectLacks_IsInvalidWithTheTranslationLanguages()
    {
        _languages.Settings.Source = "de_DE";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args("de_DE", "sv_SE", "fr_FR"))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(
            [
                "languages[0] is the project's source language, which EPLAN cannot remove.",
                "languages[2] is not a translation language of the project; metadata.candidates lists them.",
            ],
            error.Metadata!["violations"].Split('\n'));
        Assert.Equal("de_DE\nen_US\nfi_FI\nsv_SE\nzh_CN", error.Metadata["candidates"]);
    }

    [Fact]
    public void Execute_BacksUpThenRemovesInOneTransaction_AndReadsTheSettingsBack()
    {
        LanguageRemoveData data = Execute(Args("sv_SE"));

        Assert.Equal(("Line1", 2, null), (data.Project, data.TextsAffected, data.Warning));
        Assert.Equal(["sv_SE"], data.Removed);
        Assert.Equal(["de_DE", "en_US", "fi_FI", "sv_SE", "zh_CN"], data.Before.Translation);
        Assert.Equal(["de_DE", "en_US", "fi_FI", "zh_CN"], data.After.Translation);
        Assert.Equal(["zh_CN"], data.After.Display); // EPLAN changed them (S23)
        Assert.Single(_backup.Copies);
        Assert.DoesNotContain(_remover.Stored, text => text.Raw.Contains("sv_SE@", StringComparison.Ordinal));
        Assert.Equal(["Begin AI: eplan_language_remove", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.DoesNotContain(_languages.Calls, call => call.StartsWith("Write", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_WhenEplanFailsOnTheSecondLanguage_TheTranslationsAreRolledBackAndTheSettingsWrittenBack()
    {
        _remover.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not remove fi_FI.");
        _remover.FailOn = "fi_FI";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args("sv_SE", "fi_FI"))).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(["sv_SE", "fi_FI"], _remover.Removals);
        Assert.Equal("de_DE@Motor;en_US@Motor;sv_SE@Motor;zh_CN@电机;", _remover.Stored[0].Raw);
        Assert.Equal((Before, "##_##;"), (_languages.Settings.Translation, _languages.Settings.Display));
        Assert.Equal(["Begin AI: eplan_language_remove", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_ALanguageEplanLeftInTheList_IsRolledBack()
    {
        _remover.Keep = "sv_SE";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args("sv_SE"))).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack, "sv_SE"), (error.Reason, error.Execution, error.Metadata!["notRemoved"]));
        Assert.Contains("sv_SE@Kontaktor;", _remover.Stored[1].Raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AWriteBackThatFails_IsOutcomeUnknownAndPointsToTheBackup()
    {
        _remover.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not remove fi_FI.");
        _remover.FailOn = "fi_FI";
        _languages.WriteFailure = new InvalidOperationException("the setting cannot be written");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args("sv_SE", "fi_FI"))).Error;

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        Assert.Contains("or to open the backup copy (metadata.backup)", error.Message, StringComparison.Ordinal);
        Assert.Equal(("de_DE\nen_US\nfi_FI\nsv_SE\nzh_CN", "##_##"), (error.Metadata!["translation"], error.Metadata["display"]));
    }

    [Fact]
    public void Execute_AfterATextChangedMeanwhile_IsPlanStaleWithoutABackup()
    {
        LanguageRemoveArgs args = Args("sv_SE");
        args.Fingerprint = Plan(args).Fingerprint;
        _remover.Stored[0].Raw += "sv_SE@Ny;"; // the engineer, meanwhile

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_backup.Copies);
        Assert.Empty(_remover.Removals);
    }

    [Fact]
    public void Execute_AChangeToATextWithoutTheLanguages_KeepsThePlan()
    {
        LanguageRemoveArgs args = Args("sv_SE");
        args.Fingerprint = Plan(args).Fingerprint;
        _remover.Stored[2].Raw = "de_DE@Neu;";

        Assert.Equal(["sv_SE"], Run(args).Removed);
    }

    [Fact]
    public void Execute_WhenTheBackupFails_NothingIsRemoved()
    {
        _backup.Failure = new IOException("disk full");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args("sv_SE"))).Error;

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_remover.Removals);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutAFingerprint_IsNotStarted()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(Args("sv_SE"))).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Fact]
    public void ACancelDuringTheFirstLanguage_StopsBeforeTheSecond_RollsBackAndWritesTheSettingsBack()
    {
        LanguageRemoveArgs args = Args("sv_SE", "fi_FI");
        args.Fingerprint = Plan(args).Fingerprint;
        args.Backup = Target();
        var router = new OperationRouter(
            [new LanguageRemoveExecuteOperation(new FakeCatalog(_project), _pages, _languages, _remover, _backup, _transactions)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));
        _remover.OnRemove = _ => router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "r1" }, "cancel");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.LanguageRemoveExecute, JObject.FromObject(args, BridgeJson.Serializer), "r1")).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(["sv_SE"], _remover.Removals);
        Assert.Equal((Before, "##_##;"), (_languages.Settings.Translation, _languages.Settings.Display));
        Assert.Contains("sv_SE@Kontaktor;", _remover.Stored[1].Raw, StringComparison.Ordinal);
    }

    private static LanguageRemoveArgs Args(params string[] languages) => new() { Project = "Line1", Languages = [.. languages] };

    private BackupTarget Target() => new() { Path = Path.Combine(_folder, "Line1.elk"), MaxSeconds = 150 };

    private WritePlan Plan(LanguageRemoveArgs args) =>
        (WritePlan)new LanguageRemovePlanOperation(new FakeCatalog(_project), _languages, _remover).Execute(args)!;

    /// <summary>Plans, then executes with the plan's fingerprint and a new backup target, as the Host does.</summary>
    private LanguageRemoveData Execute(LanguageRemoveArgs args)
    {
        args.Fingerprint = Plan(args).Fingerprint;
        return Run(args);
    }

    private LanguageRemoveData Run(LanguageRemoveArgs args)
    {
        args.Backup = Target();
        return (LanguageRemoveData)new LanguageRemoveExecuteOperation(new FakeCatalog(_project), _pages, _languages, _remover, _backup, _transactions).Execute(args)!;
    }
}
