using System.Text;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>A fake translation database with a real tab export; no EPLAN is loaded or started.</summary>
[Trait("Size", "Medium")]
public sealed class TranslationKeywordSetOperationTests
{
    private const string Database = @"$(md_translate)\Translate.db";
    private readonly Writer _writer = new();

    [Fact]
    public void Plan_SeparatesNewKeywordsFilledTranslationsAndTheOnesTheImportWouldLeaveAlone()
    {
        WritePlan plan = Plan(Args());

        Assert.Equal(OperationNames.TranslationKeywordSetPlan, new TranslationKeywordSetPlanOperation(_writer).Name);
        Assert.False(new TranslationKeywordSetPlanOperation(_writer) as IOperation is IWriteOperation);
        Assert.Equal((string.Empty, string.Empty), (plan.Project, plan.ProjectPath));
        Assert.Equal((MasterDataKinds.Translations, Database, @"C:\Translations\Translate.db"),
            (plan.MasterData!.Kind, plan.MasterData.Database, plan.MasterData.Path));
        Assert.Equal(
        [
            ("create", "New", null, null),
            ("update", "New", "zh_CN", "新"),
            ("update", "Fill", "zh_CN", "填"),
        ],
        plan.Changes.Select(change => (change.Action, change.Name, change.Property, change.NewValue)));
        Assert.All(plan.Changes, change => Assert.Equal("translation_keyword", change.Kind));
        Assert.Equal(("4", "en_US", "zh_CN", "1", "Keep [zh_CN] 保留"),
            (plan.Details!["keywords"], plan.Details["referenceLanguage"], plan.Details["languages"], plan.Details["kept"], plan.Details["keptTranslations"]));
        Assert.Equal(["export"], _writer.Calls);
    }

    [Fact]
    public void Plan_SaysNothingIsKeptWhenEveryRequestedTranslationIsEmptyOrAlreadyRight()
    {
        WritePlan plan = Plan(Args([Entry("Fill", "填"), Entry("Same", "相同")]));

        Assert.Equal(("0", false), (plan.Details!["kept"], plan.Details.ContainsKey("keptTranslations")));
        Assert.Single(plan.Changes);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnceAndReadsNothing()
    {
        TranslationKeywordSetArgs args = Args(
        [
            Entry(string.Empty, "空"),
            Entry("Fill", "填"),
            Entry("Fill", "重"),
            new TranslationKeywordEntry { Source = "Tab\tin", Translations = { ["zh_CN"] = "制表" } },
            new TranslationKeywordEntry { Source = "Codes", Translations = { ["ZH"] = "大写" } },
            new TranslationKeywordEntry { Source = "Empty", Translations = { ["zh_CN"] = string.Empty } },
        ]);
        args.ReferenceLanguage = "english";

        BridgeError error = Error(() => Plan(args));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(
        [
            "'reference_language' must be a language code such as en_US.",
            "'keywords[0].source' must be 1 to 1000 characters without a tab, line break or other control character.",
            "'keywords[2].source' repeats a source text named earlier; give each keyword once.",
            "'keywords[3].source' must be 1 to 1000 characters without a tab, line break or other control character.",
            "'keywords[4].translations' must use language codes such as zh_CN, and not 'reference_language'.",
            "'keywords[5].translations.zh_CN' must be 1 to 1000 characters without a tab, line break or other control character.",
        ],
        error.Metadata!["violations"].Split('\n'));
        Assert.Empty(_writer.Calls);
    }

    [Fact]
    public void Plan_RefusesATranslationIntoTheReferenceLanguageItself()
    {
        BridgeError error = Error(() => Plan(Args([new TranslationKeywordEntry { Source = "New", Translations = { ["en_US"] = "Itself" } }])));

        Assert.Contains("not 'reference_language'", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_RefusesALanguageTheDatabaseDoesNotHaveAndNamesTheOnesItHas()
    {
        BridgeError error = Error(() => Plan(Args([new TranslationKeywordEntry { Source = "New", Translations = { ["fr_FR"] = "Nouveau" } }])));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("no language fr_FR; it has en_US, zh_CN, de_DE.", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_RefusesAnUnreadableDatabaseWithoutStartingAWrite()
    {
        _writer.Failure = "export false";

        BridgeError error = Error(() => Plan(Args()));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains("nothing was changed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_RefusesADatabaseOnSqlServerAndOneThatIsNotConfigured()
    {
        _writer.Database = "SERVER1|EplanTranslate";
        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted),
            (Error(() => Plan(Args())).Reason, Error(() => Plan(Args())).Execution));

        _writer.Database = string.Empty;
        BridgeError error = Error(() => Plan(Args()));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Fact]
    public void Fingerprint_FollowsTheKeywordsAndNotTheirOrder()
    {
        string plain = Plan(Args()).Fingerprint;
        string reordered = Plan(Args([.. Args().Keywords.AsEnumerable().Reverse()])).Fingerprint;
        string changed = Plan(Args([Entry("New", "新的"), Entry("Fill", "填"), Entry("Keep", "新译文"), Entry("Same", "相同")])).Fingerprint;

        Assert.Equal(plain, reordered);
        Assert.NotEqual(plain, changed);
    }

    [Fact]
    public void Execute_ImportsTheRemappedTableAndCountsWhatTheDatabaseActuallyTook()
    {
        TranslationKeywordSetData data = Execute(Executable());

        Assert.Equal(OperationNames.TranslationKeywordSetExecute, new TranslationKeywordSetExecuteOperation(_writer).Name);
        Assert.True(new TranslationKeywordSetExecuteOperation(_writer) is IWriteOperation);
        Assert.Equal((Database, "en_US", 4, 1, 1, 1),
            (data.Database, data.ReferenceLanguage, data.Keywords, data.Added, data.Filled, data.Kept));
        Assert.Equal(["export", "import:en_US", "export"], _writer.Calls);
        // The export's own header, COMMENT included, with the columns the request does not fill left empty (A47).
        Assert.Equal("en_US\tzh_CN\tde_DE\tCOMMENT\r\nNew\t新\t\t\r\nFill\t填\t\t\r\nKeep\t新译文\t\t\r\nSame\t相同\t\t\r\n", _writer.ImportText);
    }

    [Fact]
    public void Execute_RefusesWithoutThePlansFingerprintAndDatabase()
    {
        Assert.Equal(ErrorReason.InvalidArgument, Error(() => Execute(Args())).Reason);
        Assert.Empty(_writer.Calls);
    }

    [Fact]
    public void Execute_RefusesWhenTheKeywordsChangedAfterPlanning()
    {
        TranslationKeywordSetArgs args = Executable();
        args.Keywords[0].Translations["zh_CN"] = "改了";

        BridgeError error = Error(() => Execute(args));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_writer.Calls);
    }

    [Fact]
    public void Execute_RefusesWhenTheConfiguredDatabaseChangedAfterPlanning()
    {
        TranslationKeywordSetArgs args = Executable();
        args.Database = @"$(md_translate)\Other.db";

        Assert.Equal(ErrorReason.PlanStale, Error(() => Execute(args)).Reason);
        Assert.Empty(_writer.Calls);
    }

    [Fact]
    public void Execute_ReportsAnUnknownOutcomeOnceTheImportHasStarted()
    {
        TranslationKeywordSetArgs args = Executable();
        _writer.Failure = "import throw";

        BridgeError error = Error(() => Execute(args));

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown, TranslationKeywordSetExecuteOperation.UnknownMessage),
            (error.Reason, error.Execution, error.Message));
    }

    [Fact]
    public void Execute_ReportsAnUnknownOutcomeWhenTheDatabaseDidNotTakeAKeyword()
    {
        TranslationKeywordSetArgs args = Executable();
        _writer.Failure = "missing entry";

        Assert.Equal(ErrorReason.OutcomeUnknown, Error(() => Execute(args)).Reason);
    }

    [Fact]
    public void Execute_SaysNothingWasChangedWhenTheExportBeforeTheImportFails()
    {
        TranslationKeywordSetArgs args = Executable();
        _writer.Failure = "before throw";

        BridgeError error = Error(() => Execute(args));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    private static TranslationKeywordEntry Entry(string source, string chinese) =>
        new() { Source = source, Translations = { ["zh_CN"] = chinese } };

    private static TranslationKeywordSetArgs Args(List<TranslationKeywordEntry>? keywords = null) => new()
    {
        Keywords = keywords ?? [Entry("New", "新"), Entry("Fill", "填"), Entry("Keep", "新译文"), Entry("Same", "相同")],
    };

    private TranslationKeywordSetArgs Executable()
    {
        TranslationKeywordSetArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        args.Database = Database;
        _writer.Calls.Clear();
        return args;
    }

    private WritePlan Plan(TranslationKeywordSetArgs args) => (WritePlan)new TranslationKeywordSetPlanOperation(_writer).Execute(args)!;

    private TranslationKeywordSetData Execute(TranslationKeywordSetArgs args) => (TranslationKeywordSetData)new TranslationKeywordSetExecuteOperation(_writer).Execute(args)!;

    private static BridgeError Error(Func<object> call) => Assert.Throws<BridgeErrorException>(() => call()).Error;

    private sealed class Writer : ITranslationDatabaseWriter
    {
        private const string BeforeText = "en_US\tzh_CN\tde_DE\tCOMMENT\nFill\t\tFüllen\tc1\nKeep\t保留\tBehalten\t\nSame\t相同\tGleich\t\n";
        private const string AfterText = "zh_CN\ten_US\tde_DE\tCOMMENT\n新\tNew\t\t\n填\tFill\tFüllen\tc1\n保留\tKeep\tBehalten\t\n相同\tSame\tGleich\t\n";

        public string Database { get; set; } = TranslationKeywordSetOperationTests.Database;

        public string Failure { get; set; } = string.Empty;

        public List<string> Calls { get; } = [];

        public string? ImportText { get; private set; }

        public ConfiguredDatabase Configured() => new() { Database = Database, Path = @"C:\Translations\Translate.db" };

        public bool Export(string file)
        {
            bool after = Calls.Exists(call => call.StartsWith("import:", StringComparison.Ordinal));
            string phase = after ? "after" : "before";
            Calls.Add("export");
            if (Failure == phase + " throw")
            {
                throw new InvalidOperationException("export failed");
            }

            // "missing entry" keeps the database as it was, so the read-back misses every requested keyword.
            File.WriteAllText(file, after && Failure != "missing entry" ? AfterText : BeforeText, Encoding.Unicode);
            return Failure != "export false" && Failure != phase + " false";
        }

        public bool Import(string file, string referenceLanguage)
        {
            Calls.Add("import:" + referenceLanguage);
            ImportText = File.ReadAllText(file, Encoding.Unicode);
            return Failure == "import throw"
                ? throw new InvalidOperationException("import failed after partially changing the database")
                : Failure != "import false";
        }
    }
}
