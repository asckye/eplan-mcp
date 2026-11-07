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
        _inserter.Pages["=GAA+A1&EFA2/1"] = new MacroTargetPage { Name = "=GAA+A1&EFA2/1", Type = "CircuitSingleLine" };
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
    public void Values_PlanListsApplicationsInNameOrderAndExecuteAppliesTheSameValues()
    {
        _macros.Content.Placeholders =
        [
            new() { Name = "#<EES_F_CH1>", ValueSets = ["3A", "6A"] },
            new() { Name = "#<EES_F_CH2>", ValueSets = ["3A", "6A"] },
        ];
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Values = new() { ["#<EES_F_CH2>"] = "3A", ["#<EES_F_CH1>"] = "6A" };
        WritePlan plan = Plan(args);

        Assert.Equal(3, plan.Changes.Count);
        Assert.Equal(
            [("update", "placeholder", "#<EES_F_CH1>", "value set", "6A"), ("update", "placeholder", "#<EES_F_CH2>", "value set", "3A")],
            plan.Changes.Skip(1).Select(change => (change.Action, change.Kind, change.Name, change.Property, change.NewValue)));
        _inserter.ApplyValues = values =>
        {
            Assert.Contains("Begin AI: eplan_macro_insert", _transactions.Calls);
            Assert.DoesNotContain("Commit", _transactions.Calls);
            Assert.Equal("6A", values["#<EES_F_CH1>"]);
        };
        args.Fingerprint = plan.Fingerprint;
        MacroInsertData data = Execute(args);

        Assert.Equal(args.Values, _inserter.Values);
        Assert.Equal([("#<EES_F_CH1>", "6A"), ("#<EES_F_CH2>", "3A")], data.Values.Select(value => (value.Placeholder, value.ValueSet)));
        Assert.Contains("Commit", _transactions.Calls);
    }

    [Fact]
    public void Values_ChangeTheFingerprintAndDictionaryOrderDoesNot()
    {
        _macros.Content.Placeholders =
        [
            new() { Name = "Channel1", ValueSets = ["3A", "6A"] },
            new() { Name = "Channel2", ValueSets = ["3A", "6A"] },
        ];
        MacroInsertArgs args = Args(NewPage, 40, 280);
        string omitted = Plan(args).Fingerprint;
        args.Values = new() { ["Channel2"] = "3A", ["Channel1"] = "6A" };
        string requested = Plan(args).Fingerprint;
        Assert.NotEqual(omitted, requested);
        args.Values = new() { ["Channel1"] = "6A", ["Channel2"] = "3A" };
        Assert.Equal(requested, Plan(args).Fingerprint);
        args.Fingerprint = requested;
        args.Values["Channel1"] = "3A";

        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_inserter.Inserts);
    }

    [Fact]
    public void Values_FingerprintKeepsDelimiterCharactersUnambiguous()
    {
        _macros.Content.Placeholders =
        [
            new() { Name = "A", ValueSets = ["B\tC"] },
            new() { Name = "A\tB", ValueSets = ["C"] },
        ];
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Values = new() { ["A"] = "B\tC" };
        string first = Plan(args).Fingerprint;
        args.Values = new() { ["A\tB"] = "C" };

        Assert.NotEqual(first, Plan(args).Fingerprint);
    }

    [Theory]
    [InlineData("unknown", "6A", false)]
    [InlineData("#<EES_F_CH1>", "unknown", false)]
    [InlineData("#<EES_F_CH1>", "6A", true)]
    public void Values_InvalidSelectionIsRefusedByThePlan(string name, string valueSet, bool duplicate)
    {
        var holder = new MacroPlaceholderRecord { Name = "#<EES_F_CH1>", ValueSets = ["6A"] };
        _macros.Content.Placeholders = duplicate ? [holder, holder] : [holder];
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Values = new() { [name] = valueSet };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/values/" + name, error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_inserter.Inserts);
    }

    [Fact]
    public void Values_MoreThanTwentyEntriesIsRefusedByThePlan()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Values = Enumerable.Range(0, 21).ToDictionary(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "6A");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/values", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_inserter.Inserts);
    }

    [Fact]
    public void Values_PartMacroUsesOnlyTheSelectedRepresentationAndVariant()
    {
        MacroInsertArgs args = Args("=GAA+A1&EFS2/1", 40, 280);
        args.Macro = null;
        args.Part = "PXC.2861344";
        args.Representation = "Overview";
        args.Variant = 2;
        args.Values = new() { ["#<EES_F_CH1>"] = "6A" };
        _macros.PartSource = (part, _) => new() { Part = part, PartVariant = "1", Macro = Macro };
        _macros.ReadSelection = (representation, variant) => new MacroContent
        {
            Representations = [new("Overview", [2])],
            Width = 20,
            Height = 20,
            Placeholders = representation == "Overview" && variant == 2
                ? [new() { Name = "#<EES_F_CH1>", ValueSets = ["6A"] }]
                : [],
        };
        args.Fingerprint = Plan(args).Fingerprint;

        MacroInsertData data = Execute(args);

        Assert.Equal(("Overview", 2), _macros.Selection);
        Assert.Equal("6A", Assert.Single(data.Values).ValueSet);
        Assert.Equal("6A", _inserter.Values!["#<EES_F_CH1>"]);
    }

    [Fact]
    public void Values_ApplyFailureRollsBackTheInsertedMacro()
    {
        _macros.Content.Placeholders = [new() { Name = "#<EES_F_CH1>", ValueSets = ["6A"] }];
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Values = new() { ["#<EES_F_CH1>"] = "6A" };
        args.Fingerprint = Plan(args).Fingerprint;
        _inserter.ApplyValues = _ => throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Cannot apply record");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Empty(_inserter.Pages[NewPage].Objects);
        Assert.Equal(["+A1-WG6"], _devices.Functions.Select(function => function.Name));
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void Values_OmittedLeavesResultEmptyAndDoesNotApplyRecords()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Fingerprint = Plan(args).Fingerprint;
        _inserter.ApplyValues = _ => throw new InvalidOperationException("Unexpected application");

        Assert.Empty(Execute(args).Values);
        Assert.Null(_inserter.Values);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MacroBox_IsBoundToPlanAndPassedIntoTheInsertionTransaction(bool retain)
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.MacroBox = retain;
        _macros.Content.MacroBoxCount = 1;
        WritePlan plan = Plan(args);
        Assert.Equal(retain ? "28" : "27", plan.Details!["objectCount"]);
        Assert.Equal(!retain, plan.Changes[0].NewValue!.Contains("without macro box", StringComparison.Ordinal));
        args.Fingerprint = plan.Fingerprint;
        Execute(args);
        Assert.Equal(retain, _inserter.MacroBox);
        Assert.Single(_transactions.Calls, c => c.StartsWith("Begin ", StringComparison.Ordinal));
        Assert.Contains("Commit", _transactions.Calls);
    }

    [Fact]
    public void MacroBoxChangedAfterPlan_IsStaleBeforeInsertion()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Fingerprint = Plan(args).Fingerprint;
        args.MacroBox = false;
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_inserter.Inserts);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void LayoutGuard_MacroEnvelopeOverSymbol_RefusesWithSuggestion()
    {
        MacroTargetPage page = LayoutFixture.Target(_inserter.Pages[SamplePage])!;
        page.Layout!.Page.Placements.Add(LayoutGuardTests.Row("Function", 9, 40, 100));
        BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => Plan(Args(SamplePage, 40, 290)));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Contains("R2", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Suggested position", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void LayoutGuardWarn_MacroEnvelopeOverSymbol_PlansWithAWarning()
    {
        MacroTargetPage page = LayoutFixture.Target(_inserter.Pages[SamplePage])!;
        page.Layout!.Page.Placements.Add(LayoutGuardTests.Row("Function", 9, 40, 100));
        MacroInsertArgs args = Args(SamplePage, 40, 290);
        args.LayoutGuard = "warn";

        Assert.Contains("R2", Plan(args).Details!["layoutWarnings"], StringComparison.Ordinal);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Plan_IsOneMacroChange_WithTheMacroDevicesForTheDryRun()
    {
        WritePlan plan = Plan(Args(NewPage, 40, 280));

        Assert.Equal(("Line1", @"D:\Scratch\Line1"), (plan.Project, plan.ProjectPath));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(
            ("create", "macro", NewPage, "Contactor_Feedback_Control.ema at (40, 280), numbering number, 3 devices"),
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
            ["'page' must be Circuit for MultiLine, but '=GAA+A1&EFS2/1' is Overview.", "'macro' has no MultiLine representation (available: ArticlePlacement); choose another macro."],
            violations);
    }

    [Fact]
    public void Plan_AVariantTheMacroLacks_ListsItsVariants()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Variant = 6;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal("'variant' 6 does not exist; the macro's MultiLine variants are 0, 1, 2, 3, 4, 5.", error.Metadata!["violations"]);
    }

    [Fact]
    public void SingleLine_GoesOnACircuitSingleLinePage()
    {
        MacroInsertArgs args = Args("=GAA+A1&EFA2/1", 40, 200);
        args.Representation = "SingleLine";
        _macros.ReadSelection = (_, _) => new MacroContent { Representations = [new("SingleLine", [0])], Width = 20, Height = 20, ObjectCount = 196 };
        args.Fingerprint = Plan(args).Fingerprint;
        Execute(args);
        Assert.Equal(("SingleLine", null, null), _inserter.Source);
        MacroInsertArgs circuit = Args(NewPage, 40, 280);
        circuit.Representation = "SingleLine";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(circuit)).Error.Reason);
    }

    [Fact]
    public void Overview_UsesTheSelectedRepresentationAndVariantForLayoutAndInsertion()
    {
        MacroInsertArgs args = Args("=GAA+A1&EFS2/1", 40, 200);
        args.Representation = "Overview";
        args.Variant = 2;
        _macros.ReadSelection = (representation, variant) => new MacroContent
        {
            Representations = [new("MultiLine", [0]), new("Overview", [2])],
            Width = representation == "Overview" && variant == 2 ? 20 : 1000,
            Height = 20,
            ObjectCount = 196,
        };

        WritePlan plan = Plan(args);
        Assert.Equal(("Overview", 2), _macros.Selection);
        Assert.Equal("196", plan.Details!["objectCount"]);
        Assert.Contains("representation Overview", plan.Changes[0].NewValue, StringComparison.Ordinal);
        args.Fingerprint = plan.Fingerprint;
        Execute(args);
        Assert.Equal(("Overview", null, null), _inserter.Source);
    }

    [Theory]
    [InlineData("ArticlePlacement3D")]
    [InlineData("Bogus")]
    public void Plan_UnsupportedRepresentation_IsInvalidArgument(string representation)
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Representation = representation;
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Null(_macros.Opened);
    }

    [Fact]
    public void PartGroupMacro_ResolvesSafePathAndReadsFreshCommittedArticleReferences()
    {
        MacroInsertArgs args = Args("=GAA+A1&EFS2/1", 40, 280);
        args.Macro = null;
        args.Part = "PXC.2861344";
        args.PartVariant = "2";
        args.Representation = "Overview";
        _macros.Content.Representations = [new("Overview", [0])];
        _macros.PartSource = (part, variant) =>
        {
            Assert.Equal(("PXC.2861344", "2"), (part, variant));
            return new() { Part = part, PartVariant = variant!, Macro = Macro };
        };
        _inserter.CommittedReadback = placed =>
        {
            Assert.Equal("Dispose", _transactions.Calls.Last());
            return new() { Functions = placed.Functions, ObjectCount = 196, ArticleReferences = [new() { Tag = "+A1-A1", PartNumber = args.Part, Variant = "2", Count = 1 }] };
        };

        args.Fingerprint = Plan(args).Fingerprint;
        MacroInsertData data = Execute(args);

        Assert.Equal(("Overview", "PXC.2861344", "2"), _inserter.Source);
        Assert.Equal(Macro, data.Macro);
        Assert.Equal(("PXC.2861344", "2", 1), (data.ArticleReferences[0].PartNumber, data.ArticleReferences[0].Variant, data.ArticleReferences[0].Count));
        Assert.Equal(196, data.ObjectCount);
    }

    [Theory]
    [InlineData(null, "'part' has no ARTICLE_GROUPSYMBOLMACRO")]
    [InlineData("../outside.ema", "'macro' must be a path")]
    [InlineData("C:/outside.ema", "'macro' must be a path")]
    public void PartGroupMacro_MissingOrUnsafePath_IsInvalidArgument(string? macro, string message)
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Macro = null;
        args.Part = "PXC.2861344";
        _macros.PartSource = (_, _) => new() { Part = args.Part, PartVariant = "1", Macro = macro ?? "" };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        if (macro is null)
        { Assert.Contains("eplan_device_place", error.Message, StringComparison.Ordinal); }
        Assert.Null(_macros.Opened);
    }

    [Fact]
    public void PartAndMacroOrPartVariantWithoutPart_AreInvalidArguments()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Part = "PXC.2861344";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        args.Part = null;
        args.PartVariant = "2";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void PartResolutionChange_MakesThePlanStale()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Macro = null;
        args.Part = "PXC.2861344";
        var source = new PartMacroSource { Part = args.Part, PartVariant = "1", Macro = Macro };
        _macros.PartSource = (_, variant) => { Assert.Null(variant); return source; };
        args.Fingerprint = Plan(args).Fingerprint;
        source.PartVariant = "2";

        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_inserter.Inserts);
    }

    [Fact]
    public void CommittedReadbackFailure_RemainsCommittedWithWarning()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Fingerprint = Plan(args).Fingerprint;
        _inserter.CommittedReadback = _ => throw new InvalidOperationException("unreadable");

        Assert.Contains("could not be read back", Execute(args).Warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
    }

    [Fact]
    public void MissingLayoutMeasurements_WarnWithoutRefusingTheInsertion()
    {
        MacroTargetPage page = LayoutFixture.Target(_inserter.Pages[NewPage])!;
        page.Layout!.DrawingArea = null;
        page.Layout.Page.PageSize = null;
        MacroInsertArgs args = Args(NewPage, 40, 280);

        WritePlan plan = Plan(args);
        Assert.Contains("Layout unverified", plan.Details!["layoutWarnings"], StringComparison.Ordinal);
        args.Fingerprint = plan.Fingerprint;
        Assert.Contains("Layout unverified", Execute(args).Warning, StringComparison.Ordinal);
        Assert.Contains("Commit", _transactions.Calls);
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
    public void Plan_OnAPageOfUnknownSize_RefusesOutsideTrustedDrawingArea()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(NewPage, 1000, 1000))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
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
        MacroInsertArgs args = Args(NewPage, 40, 280);
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

        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(Args(NewPage, 40, 280))).Error.Reason);
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
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Variant = 2;
        args.Fingerprint = Plan(args).Fingerprint;

        MacroInsertData data = Execute(args);

        Assert.Equal(("Line1", NewPage, Macro, false), (data.Project, data.Page, data.Macro, data.PageSizeKnown));
        Assert.Contains("before write", data.Warning, StringComparison.Ordinal);
        Assert.Contains("after write", data.Warning, StringComparison.Ordinal);
        Assert.Equal(
            [("+A1-WG7", "-WG7", "Cable"), ("+A1-XG9:?1", "-XG9", "Terminal"), ("=GAA+A1-?QA1", "-?QA1", "NoContact")],
            data.Devices.Select(d => (d.Tag, d.VisibleTag, d.Category)));
        Assert.Equal((3, 13), (data.DeviceCount, data.ObjectCount));
        Assert.Equal((NewPage, Path.Combine([_root, .. Macro.Split('/')]), 2, 40.0, 280.0, true), Assert.Single(_inserter.Inserts));
        Assert.Equal(["Begin AI: eplan_macro_insert", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_ManyDevices_ListsTheFirstFiftyAndCountsAll()
    {
        _inserter.Placed = _ => [.. Enumerable.Range(1, 60).Select(i => new MacroFunctionRecord { Name = "+A1-XG" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Category = "Terminal" })];
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Fingerprint = Plan(args).Fingerprint;

        MacroInsertData data = Execute(args);

        Assert.Equal((50, 60), (data.Devices.Count, data.DeviceCount));
    }

    [Fact]
    public void Execute_WithKeep_ADeviceMergedWithAnExistingOne_IsAlreadyExistsAndRolledBack()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
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
    public void Execute_WithNumber_ARepeatedPlcTerminal_IsNoMerge()
    {
        // S10: PLC terminals keep the macro's structure in every mode, so a second insert repeats =K1+A1:1.4.
        _devices.Functions.Add(new FunctionRecord { Name = "=K1+A1:1.4", IsMain = true, Category = "PLCTerminal" });
        _inserter.Placed = _ => [new() { Name = "=K1+A1:1.4", Category = "PLCTerminal", Main = true }];
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Fingerprint = Plan(args).Fingerprint;

        MacroInsertData data = Execute(args);

        Assert.Equal("=K1+A1:1.4", Assert.Single(data.Devices).Tag);
        Assert.Equal(2, _devices.Functions.Count(f => f.Name == "=K1+A1:1.4"));
        Assert.Contains("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_TheSameSymbolsOnTopOfEachOther_IsInvalidArgumentAndRolledBack()
    {
        MacroInsertArgs args = Args(NewPage, 40, 280);
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
        MacroInsertArgs args = Args(NewPage, 40, 280);
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
        MacroInsertArgs args = Args(NewPage, 40, 280);
        args.Fingerprint = Plan(args).Fingerprint;
        var router = new OperationRouter([new MacroInsertExecuteOperation(_catalog, _pages, _devices, _macros, _inserter, _transactions)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        router.Invoke(OperationNames.MacroInsertExecute, JObject.FromObject(args, BridgeJson.Serializer), "r1");

        Assert.Equal(OutcomeStatus.Committed, router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = "r1" }, "c")["status"]!.ToString());
        Assert.Contains(OperationNames.MacroInsertExecute, router.SupportedOperations);
    }

    [Fact]
    public void Single_OmittedScalarFieldsKeepTheirPreviousDefaults()
    {
        var args = new MacroInsertArgs { Project = "Line1", Page = NewPage, Macro = Macro };

        MacroInsertRequest request = MacroInsertRequest.Check(args, _catalog, _pages, _macros, _inserter, enforceLayout: false);

        Assert.Equal((0d, 0d, 0, true), (request.X, request.Y, request.Variant, request.MacroBox));
    }

    [Fact]
    public void Batch_ReportsEachItemsArgumentAndPlaceholderProblemsTogether()
    {
        _macros.Content.Placeholders = [new() { Name = "#<Function_text>", ValueSets = ["Forward control", "Reverse control"] }];
        MacroInsertArgs args = Batch();
        args.Items![0].Page = string.Empty;
        args.Items[0].Variant = -1;
        args.Items[1].Numbering = "invalid";
        args.Items[2].Values = new() { ["missing/name"] = "Reverse control" };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/items/0/page", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Contains("/items/0/variant", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Contains("/items/1/numbering", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Contains("/items/2/values/missing~1name", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Empty(_inserter.Inserts);
    }

    [Fact]
    public void Batch_MixedErrorsKeepFirstSpecificReasonAndAllItemViolations()
    {
        MacroInsertArgs args = Batch();
        args.Items![0].Variant = -1;
        args.Items[1].Page = "=MA1+UC1&EFS1/404";
        args.Items[2].Numbering = "invalid";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.PageNotFound, error.Reason);
        Assert.Equal("1", error.Metadata!["item"]);
        Assert.Equal("=MA1+UC1&EFS1/404", error.Metadata["page"]);
        Assert.StartsWith("No page has this name", error.Message, StringComparison.Ordinal);
        Assert.Contains("/items/0/variant", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Contains("/items/1:", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Contains("/items/2/numbering", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Empty(_inserter.Inserts);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Batch_OverlappingItemsOnOnePageAreRefused()
    {
        MacroInsertArgs args = Batch();
        args.Items![1].X = args.Items[2].X;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("R2", error.Message, StringComparison.Ordinal);
        Assert.Contains("/items/1:", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Contains("/items/2:", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(error.Metadata["layoutChecks"]));
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Batch_OverlappingItemsOnOnePageWarnWhenRequested()
    {
        MacroInsertArgs args = Batch();
        args.Items![1].X = args.Items[2].X;
        args.LayoutGuard = "warn";

        Assert.Contains("R2", Plan(args).Details!["layoutWarnings"], StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_LayoutViolationsAcrossPagesAreReportedTogether()
    {
        MacroInsertArgs args = Batch();
        args.Items!.Insert(1, new() { Page = NewPage, Macro = Macro, X = 24, Y = 280 });
        args.Items[2].X = args.Items[3].X;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Contains("/items/0:", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Contains("/items/1:", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Contains("/items/2:", error.Metadata["violations"], StringComparison.Ordinal);
        Assert.Contains("/items/3:", error.Metadata["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Batch_EqualOutlinesOnDifferentPagesPass()
    {
        MacroInsertArgs args = Batch();
        args.Items!.RemoveAt(2);
        args.Items[1].X = args.Items[0].X;

        Assert.Equal(2, Plan(args).Changes.Count);
    }

    [Fact]
    public void Batch_ThreeInsertionsCommitOnceAndReadBackEachItemsAppliedValues()
    {
        _macros.Content.Placeholders = [new() { Name = "#<Function_text>", ValueSets = ["Forward control", "Reverse control"] }];
        MacroInsertArgs args = Batch();
        args.Items![1].Values = new() { ["#<Function_text>"] = "Forward control" };
        args.Items[2].Values = new() { ["#<Function_text>"] = "Reverse control" };
        var applied = new List<string>();
        _inserter.ApplyValues = values => applied.Add(values["#<Function_text>"]);
        int readbacks = 0;
        _inserter.CommittedReadback = placed =>
        {
            Assert.Contains("Commit", _transactions.Calls);
            readbacks++;
            return placed;
        };
        WritePlan plan = Plan(args);
        Assert.Equal([NewPage, SamplePage, SamplePage + " #<Function_text>", SamplePage, SamplePage + " #<Function_text>"], plan.Changes.Select(change => change.Name));
        args.Fingerprint = plan.Fingerprint;

        MacroInsertData data = Execute(args);

        Assert.Equal(["Forward control", "Reverse control"], applied);
        Assert.Equal(3, readbacks);
        Assert.Equal(3, data.ItemCount);
        Assert.Equal(9, data.DeviceCount);
        Assert.Equal([NewPage, SamplePage, SamplePage], data.Items!.Select(item => item.Page));
        Assert.Equal([0, 1, 1], data.Items!.Select(item => item.ValueCount));
        Assert.All(data.Items!, item =>
        {
            Assert.Empty(item.Values);
            Assert.Empty(item.Devices);
        });
        Assert.Equal(["Begin AI: eplan_macro_insert", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Batch_FailureInThirdItemRollsBackEverythingWithZeroBasedItem(bool knownFailure)
    {
        MacroInsertArgs args = Batch();
        args.Fingerprint = Plan(args).Fingerprint;
        _inserter.Placed = number =>
        {
            if (_inserter.Inserts.Count == 3)
            {
                if (knownFailure)
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Third item failed");
                }
                throw new InvalidOperationException("Third item failed");
            }
            return Numbered(number);
        };
        string[] existingObjects = [.. _inserter.Pages[SamplePage].Objects];

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal("2", error.Metadata!["item"]);
        Assert.Equal("Third item failed", error.Message);
        Assert.Empty(_inserter.Pages[NewPage].Objects);
        Assert.Equal(existingObjects, _inserter.Pages[SamplePage].Objects);
        Assert.Equal(["+A1-WG6"], _devices.Functions.Select(function => function.Name));
        Assert.Equal(["Begin AI: eplan_macro_insert", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Batch_FingerprintIncludesItemOrderAndCoordinates()
    {
        MacroInsertArgs args = Batch();
        string original = Plan(args).Fingerprint;
        args.Items![2].X += 4;
        string moved = Plan(args).Fingerprint;
        Assert.NotEqual(original, moved);
        args.Items[2].X -= 4;
        args.Items.Reverse();
        Assert.NotEqual(original, Plan(args).Fingerprint);
    }

    private static MacroInsertArgs Batch() => new()
    {
        Project = "Line1",
        Items =
        [
            new() { Page = NewPage, Macro = Macro, X = 24, Y = 280 },
            new() { Page = SamplePage, Macro = Macro, X = 24, Y = 280 },
            new() { Page = SamplePage, Macro = Macro, X = 76, Y = 280 },
        ],
    };

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
