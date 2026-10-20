using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// macro.insert.plan and macro.insert.execute against fake pages and S10's macro in a temporary macro directory
/// (docs/tools/eplan_macro_insert.md, sections 2, 4, 5 and 7): the checks, the fingerprint (Q6) and the rollback of a
/// merge or a refused insert (Q2). Page /60 is new and empty, so EPLAN cannot tell its size (A14); page /1 has S10's
/// sample size. The project has the cable +A1-WG6 that the macro's cable becomes with numbering keep.
/// </summary>
[Trait("Size", "Medium")]
public sealed class MacroInsertOperationTests : IDisposable
{
    private const string Macro = "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema";
    private const string NewPage = "=GAA+A1&EFS1/60";
    private const string SamplePage = "=GAA+A1&EFS1/1";

    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-macro-insert-").FullName;
    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _pages = new();
    private readonly FakeDeviceReader _devices = new();
    private readonly FakeMacroReader _macros;
    private readonly FakeMacroInserter _inserter;
    private readonly FakeTransactions _transactions = new();

    public MacroInsertOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        string file = Path.Combine(_root, Macro);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "macro");
        _macros = new FakeMacroReader(_root);
        _macros.Content.Functions.AddRange(
        [
            new() { Name = "+-WG6", VisibleName = "-WG6", Category = "Cable", Main = true },
            new() { Name = "+-WG6", Category = "Wire" },
            new() { Name = "+-XG8:?", VisibleName = "-XG8", Category = "Terminal", Main = true },
            new() { Name = "=+A1-QA1", VisibleName = "-QA1", Category = "NoContact" },
        ]);
        _inserter = new FakeMacroInserter(_devices);
        _inserter.Pages[NewPage] = new MacroTargetPage { Name = NewPage, Type = "Circuit" };
        _inserter.Pages[SamplePage] = new MacroTargetPage { Name = SamplePage, Type = "Circuit", Width = 420, Height = 297.02, Objects = ["1/1/1/0", "1/1/2/0"] };
        _inserter.Pages["=GAA+A1&EFS2/1"] = new MacroTargetPage { Name = "=GAA+A1&EFS2/1", Type = "Overview" };
        _pages.Pages.AddRange(_inserter.Pages.Keys.Select(name => new PageSummary { Name = name }));
        _devices.Functions.Add(new FunctionRecord { Name = "+A1-WG6", IsMain = true, Category = "Cable" });
        _inserter.Placed = Numbered;
        _transactions.Snapshot = () =>
        {
            FunctionRecord[] functions = [.. _devices.Functions];
            Dictionary<string, string[]> objects = _inserter.Pages.ToDictionary(p => p.Key, p => p.Value.Objects.ToArray());
            return () =>
            {
                _devices.Functions.Clear();
                _devices.Functions.AddRange(functions);
                foreach ((string page, string[] ids) in objects)
                {
                    _inserter.Pages[page].Objects = [.. ids];
                }
            };
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Plan_IsOneMacroChange_WithTheMacroDevicesForTheDryRun()
    {
        WritePlan plan = Plan(Args(NewPage, 40, 200));

        Assert.Equal(("Line1", @"D:\Scratch\Line1"), (plan.Project, plan.ProjectPath));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(
            ("create", "macro", NewPage, "Contactor_Feedback_Control.ema at (40, 200), numbering number, 3 devices"),
            (change.Action, change.Kind, change.Name, change.NewValue));
        Assert.Equal(Macro, plan.Details!["macro"]);
        Assert.Equal(["+-WG6\t-WG6\tCable", "+-XG8:?\t-XG8\tTerminal", "=+A1-QA1\t-QA1\tNoContact"], plan.Details["devices"].Split('\n'));
        Assert.Equal(("3", "28", "false"), (plan.Details["deviceCount"], plan.Details["objectCount"], plan.Details["pageSizeKnown"]));
        Assert.Equal((Path.Combine([_root, .. Macro.Split('/')]), _line1.Path), (_macros.Opened, _macros.OpenedIn));
        Assert.Empty(_inserter.Inserts);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        var args = new MacroInsertArgs { X = -1, Y = double.NaN, Variant = -1, Numbering = "renumber" };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        string[] violations = error.Metadata!["violations"].Split('\n');
        Assert.Equal(6, violations.Length);
        Assert.StartsWith("'project' is required", violations[0], StringComparison.Ordinal);
        Assert.StartsWith("'page' is required", violations[1], StringComparison.Ordinal);
        Assert.StartsWith("'macro' is required", violations[2], StringComparison.Ordinal);
        Assert.StartsWith("'x' and 'y' must be numbers from 0", violations[3], StringComparison.Ordinal);
        Assert.StartsWith("'variant' must be 0 or more", violations[4], StringComparison.Ordinal);
        Assert.Equal("'numbering' must be number or keep.", violations[5]);
        Assert.Null(_macros.Opened);
    }

    [Fact]
    public void Plan_APageOfAnotherTypeAndAMacroWithoutMultiLine_AreReportedTogether()
    {
        _macros.Content.Representations = [new("ArticlePlacement", [0])];

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args("=GAA+A1&EFS2/1", 40, 200))).Error;

        string[] violations = error.Metadata!["violations"].Split('\n');
        Assert.Equal(
            ["'page' must be a circuit page (Circuit), but '=GAA+A1&EFS2/1' is Overview; this version inserts macros on circuit pages only.", "'macro' has no MultiLine representation, which circuit pages need (it has ArticlePlacement); choose another macro, eplan_macro_get shows a macro's representations."],
            violations);
    }

    [Fact]
    public void Plan_AVariantTheMacroLacks_ListsItsVariants()
    {
        MacroInsertArgs args = Args(NewPage, 40, 200);
        args.Variant = 6;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal("'variant' 6 does not exist; the macro's MultiLine variants are 0, 1, 2, 3, 4, 5.", error.Metadata!["violations"]);
    }

    [Fact]
    public void Plan_AnOutlineOutsideAPageOfKnownSize_IsRefusedWithTheSizes()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(SamplePage, 40, 200))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("52 x 268 mm", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Contains("420 x 297.02 mm", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Equal("true", Plan(Args(SamplePage, 40, 280)).Details!["pageSizeKnown"]);
    }

    [Fact]
    public void Plan_OnAPageOfUnknownSize_ChecksOnlyThatThePointIsNotNegative()
    {
        WritePlan plan = Plan(Args(NewPage, 1000, 1000));

        Assert.Equal("false", plan.Details!["pageSizeKnown"]);
    }

    [Fact]
    public void Plan_FindsThePageIgnoringCase_OrSaysWhichPagesMatchOrThatNoneDoes()
    {
        Assert.Equal(NewPage, Plan(Args("=gaa+a1&efs1/60", 40, 280)).Changes[0].Name);
        _pages.Pages.Add(new PageSummary { Name = "=gaa+a1&EFS1/60" });
        BridgeError ambiguous = Assert.Throws<BridgeErrorException>(() => Plan(Args("=GAA+A1&efs1/60", 40, 280))).Error;
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Plan(Args("=GAA+A1&EFS1/61", 40, 280))).Error;

        Assert.Equal(ErrorReason.AmbiguousName, ambiguous.Reason);
        Assert.Equal([NewPage, "=gaa+a1&EFS1/60"], ambiguous.Metadata!["candidates"].Split('\n'));
        Assert.Equal((ErrorReason.PageNotFound, "=GAA+A1&EFS1/61"), (missing.Reason, missing.Metadata!["page"]));
    }

    [Theory]
    [InlineData(@"\\server\share\x.ema", ErrorReason.InvalidArgument)]
    [InlineData("EPLAN_Macro/../../x.ema", ErrorReason.InvalidArgument)]
    [InlineData("EPLAN_Macro/Missing.ema", ErrorReason.MacroNotFound)]
    public void Plan_AMacroPathOutsideTheRulesOrMissing_IsRefusedBeforeOpening(string macro, string reason)
    {
        MacroInsertArgs args = Args(NewPage, 40, 200);
        args.Macro = macro;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(reason, error.Reason);
        Assert.DoesNotContain(_root, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_macros.Opened);
    }

    [Fact]
    public void Plan_AReadOnlyProject_IsRefused()
    {
        _line1.ReadOnly = true;

        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(Args(NewPage, 40, 200))).Error.Reason);
        Assert.Null(_macros.Opened);
    }

    [Fact]
    public void Fingerprint_CoversThePageObjectsAndTheMacroFile()
    {
        MacroInsertArgs args = Args(SamplePage, 40, 280);
        string before = Plan(args).Fingerprint;
        _inserter.Pages[SamplePage].Objects.Reverse();
        string reordered = Plan(args).Fingerprint;
        _devices.Functions.Add(new FunctionRecord { Name = "+A1-XG8", IsMain = true }); // numbering number does not look at tags
        string tagAdded = Plan(args).Fingerprint;
        _inserter.Pages[SamplePage].Objects.Add("1/1/3/0");
        string objectAdded = Plan(args).Fingerprint;
        File.SetLastWriteTimeUtc(Path.Combine(_root, Macro), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        string macroChanged = Plan(args).Fingerprint;

        Assert.Equal(before, reordered);
        Assert.Equal(before, tagAdded);
        Assert.NotEqual(before, objectAdded);
        Assert.NotEqual(objectAdded, macroChanged);
    }

    [Fact]
    public void Fingerprint_WithKeep_CoversTheTagsAMergeWouldTake()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Numbering = "keep";
        string before = Plan(args).Fingerprint;
        _devices.Functions.Add(new FunctionRecord { Name = "=GAA+A1-KF1", IsMain = true });
        string elsewhere = Plan(args).Fingerprint;
        _devices.Functions.Add(new FunctionRecord { Name = "+A1-XG8", IsMain = true });
        string sameDevicePart = Plan(args).Fingerprint;

        Assert.Equal(before, elsewhere);
        Assert.NotEqual(before, sameDevicePart);
        Assert.Contains("numbering keep", Plan(args).Changes[0].NewValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InsertsInOneCommittedTransaction_AndListsTheDevicesAsReadBack()
    {
        MacroInsertArgs args = Args(NewPage, 40, 200);
        args.Variant = 2;
        args.Fingerprint = Plan(args).Fingerprint;

        MacroInsertData data = Execute(args);

        Assert.Equal(("Line1", NewPage, Macro, false, null), (data.Project, data.Page, data.Macro, data.PageSizeKnown, data.Warning));
        Assert.Equal(
            [("+A1-WG7", "-WG7", "Cable"), ("+A1-XG9:?1", "-XG9", "Terminal"), ("=GAA+A1-?QA1", "-?QA1", "NoContact")],
            data.Devices.Select(d => (d.Tag, d.VisibleTag, d.Category)));
        Assert.Equal((3, 13), (data.DeviceCount, data.ObjectCount));
        Assert.Equal((NewPage, Path.Combine([_root, .. Macro.Split('/')]), 2, 40.0, 200.0, true), Assert.Single(_inserter.Inserts));
        Assert.Equal(["Begin AI: eplan_macro_insert", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_ManyDevices_ListsTheFirstFiftyAndCountsAll()
    {
        _inserter.Placed = _ => [.. Enumerable.Range(1, 60).Select(i => new MacroFunctionRecord { Name = "+A1-XG" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Category = "Terminal" })];
        MacroInsertArgs args = Args(NewPage, 40, 200);
        args.Fingerprint = Plan(args).Fingerprint;

        MacroInsertData data = Execute(args);

        Assert.Equal((50, 60), (data.Devices.Count, data.DeviceCount));
    }

    [Fact]
    public void Execute_WithKeep_ADeviceMergedWithAnExistingOne_IsAlreadyExistsAndRolledBack()
    {
        MacroInsertArgs args = Args(NewPage, 40, 200);
        args.Numbering = "keep";
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.RolledBack, "+A1-WG6"), (error.Reason, error.Execution, error.Metadata!["tags"]));
        Assert.Contains("numbering 'number'", error.Message, StringComparison.Ordinal);
        Assert.False(Assert.Single(_inserter.Inserts).Number);
        Assert.Equal(["+A1-WG6"], _devices.Functions.Select(f => f.Name));
        Assert.Empty(_inserter.Pages[NewPage].Objects);
        Assert.Equal(["Begin AI: eplan_macro_insert", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_WithNumber_AMergeEplanDidAnyway_IsAlsoRolledBack()
    {
        _inserter.Placed = _ => [new() { Name = "+A1-WG6", VisibleName = "-WG6", Category = "Cable", Main = true }];
        MacroInsertArgs args = Args(NewPage, 40, 200);
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Single(_devices.Functions);
    }

    [Fact]
    public void Execute_TheSameSymbolsOnTopOfEachOther_IsInvalidArgumentAndRolledBack()
    {
        MacroInsertArgs args = Args(NewPage, 40, 200);
        args.Fingerprint = Plan(args).Fingerprint;
        _inserter.Stacked = true;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_AfterAnObjectWasAddedToThePage_IsPlanStaleAndInsertsNothing()
    {
        MacroInsertArgs args = Args(SamplePage, 40, 280);
        args.Fingerprint = Plan(args).Fingerprint;
        _inserter.Pages[SamplePage].Objects.Add("1/1/7/0"); // the engineer, while the approval waited

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Empty(_inserter.Inserts);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_WithoutAFingerprintOrWithBadArguments_IsNotStarted()
    {
        MacroInsertArgs args = Args(NewPage, 40, 200);
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        args.Fingerprint = "f";
        args.Page = "=GAA+A1&EFS2/1";
        BridgeError invalid = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (missing.Reason, missing.Execution));
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (invalid.Reason, invalid.Execution));
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_ThroughTheRouter_RecordsTheOutcome()
    {
        MacroInsertArgs args = Args(NewPage, 40, 200);
        args.Fingerprint = Plan(args).Fingerprint;
        var router = new OperationRouter([new MacroInsertExecuteOperation(_catalog, _pages, _devices, _macros, _inserter, _transactions)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        router.Invoke(OperationNames.MacroInsertExecute, JObject.FromObject(args, BridgeJson.Serializer), "r1");

        Assert.Equal(OutcomeStatus.Committed, router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = "r1" }, "c")["status"]!.ToString());
        Assert.Contains(OperationNames.MacroInsertExecute, router.SupportedOperations);
    }

    private static MacroInsertArgs Args(string page, double x, double y) => new() { Project = "Line1", Page = page, Macro = Macro, X = x, Y = y };

    /// <summary>What S10 read back after inserting the macro again with Number (A14); with None the cable keeps +A1-WG6.</summary>
    private static List<MacroFunctionRecord> Numbered(bool number) =>
    [
        new() { Name = number ? "+A1-WG7" : "+A1-WG6", VisibleName = number ? "-WG7" : "-WG6", Category = "Cable", Main = true },
        new() { Name = number ? "+A1-WG7" : "+A1-WG6", Category = "Wire" },
        new() { Name = number ? "+A1-XG9:?1" : "+A1-XG8:?", VisibleName = number ? "-XG9" : "-XG8", Category = "Terminal" },
        new() { Name = number ? "=GAA+A1-?QA1" : "=GAA+A1-QA1", VisibleName = number ? "-?QA1" : "-QA1", Category = "NoContact" },
    ];

    private WritePlan Plan(MacroInsertArgs args) =>
        (WritePlan)new MacroInsertPlanOperation(_catalog, _pages, _devices, _macros, _inserter).Execute(args)!;

    private MacroInsertData Execute(MacroInsertArgs args) =>
        (MacroInsertData)new MacroInsertExecuteOperation(_catalog, _pages, _devices, _macros, _inserter, _transactions).Execute(args)!;
}
