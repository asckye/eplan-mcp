using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ReportCreateOperationTests
{
    private const string First = "&AAB1/101";
    private const string Existing = "&AAB1/1";
    private readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };
    private readonly FakePageReader _reader = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Creator _creator;

    public ReportCreateOperationTests()
    {
        _reader.Pages.AddRange([new() { Name = Existing }, new() { Name = "=GAA+A1&EFS1/1" }]);
        _creator = new Creator(_reader, _transactions) { Reports = [Report(Existing, "old")] };
        _transactions.Snapshot = () =>
        {
            ReportPageRecord[] reports = [.. _creator.Reports];
            PageSummary[] pages = [.. _reader.Pages];
            return () =>
            {
                _creator.Reports = [.. reports];
                _reader.Pages.Clear();
                _reader.Pages.AddRange(pages);
            };
        };
    }

    [Fact]
    public void Plan_DescribesTheTypeFormFirstPageAndSchemesWithoutWriting()
    {
        ReportCreateArgs args = Args();
        args.FilterScheme = "Location designation_A3";
        args.SortScheme = "DT (full)";
        WritePlan plan = Plan(args);

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("create", "report", First, args.Type, args.Form), (change.Action, change.Kind, change.Name, change.Property, change.NewValue));
        Assert.Equal((args.FilterScheme, args.SortScheme), (plan.Details!["filter_scheme"], plan.Details["sort_scheme"]));
        Assert.Equal(("Line1", _project.Path), (plan.Project, plan.ProjectPath));
        Assert.Empty(_creator.Creates);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData("project", null)]
    [InlineData("type", "CableDiagram")]
    [InlineData("type", "tabledofcontents")]
    [InlineData("form", " ")]
    [InlineData("form", "F06_002.f06")]
    [InlineData("first_page", "bad")]
    [InlineData("first_page", "&AAB1/01")]
    [InlineData("filter_scheme", "")]
    [InlineData("sort_scheme", " ")]
    public void Plan_RejectsInvalidArgumentsBeforeAnyTransaction(string field, string? value)
    {
        ReportCreateArgs args = Args();
        switch (field)
        {
            case "project":
                args.Project = value;
                break;
            case "type":
                args.Type = value;
                break;
            case "form":
                args.Form = value;
                break;
            case "first_page":
                args.FirstPage = value;
                break;
            case "filter_scheme":
                args.FilterScheme = value;
                break;
            case "sort_scheme":
                args.SortScheme = value;
                break;
        }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'" + field + "'", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Plan_ChecksTheSchemeLengthBoundary(int length, bool valid)
    {
        ReportCreateArgs args = Args();
        args.FilterScheme = new string('f', length);
        args.SortScheme = new string('s', length);
        if (valid)
        {
            Assert.NotNull(Plan(args));
        }
        else
        {
            string violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"];
            Assert.Contains("'filter_scheme'", violations, StringComparison.Ordinal);
            Assert.Contains("'sort_scheme'", violations, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("TitlePage")]
    [InlineData("TableOfContents")]
    [InlineData("PLCDiagram")]
    [InlineData("TerminalDiagram")]
    [InlineData("PartsSumList")]
    [InlineData("PartsList")]
    [InlineData("CableOverview")]
    [InlineData("CableConnectionDiagram")]
    [InlineData("TerminalStripOverview")]
    [InlineData("DeviceList")]
    [InlineData("ConnectionList")]
    [InlineData("StructIdentifierOverview")]
    public void Plan_AcceptsTheNativeDocumentTypeNames(string type)
    {
        ReportCreateArgs args = Args();
        args.Type = type;
        Assert.NotNull(Plan(args));
    }

    [Fact]
    public void Plan_ValidatesTheFormAndDescriptionLength()
    {
        ReportCreateArgs args = Args();
        args.Form = new string('f', 501);
        args.PageDescription = new string('d', 501);
        string violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"];
        Assert.Contains("'form'", violations, StringComparison.Ordinal);
        Assert.Contains("'page_description'", violations, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AllowsAnExistingReportButRejectsACircuitFirstPageAndReadOnlyProject()
    {
        ReportCreateArgs args = Args();
        args.FirstPage = "&aab1/1";
        Assert.NotNull(Plan(args));
        args.FirstPage = "=GAA+A1&EFS1/1";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        args.FirstPage = First;
        _project.ReadOnly = true;
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void Plan_UnreadableReportFieldsDoNotRejectAnExistingFirstPage()
    {
        _creator.Reports[0].Type = string.Empty;
        _creator.Reports[0].Form = string.Empty;
        ReportCreateArgs args = Args();
        args.FirstPage = Existing;
        Assert.NotNull(Plan(args));
    }

    [Fact]
    public void Fingerprint_IsStableAcrossOrderingAndChangesForReplacedReportOrPageInventory()
    {
        string original = Plan(Args()).Fingerprint;
        _reader.Pages.Reverse();
        _creator.Reports.Reverse();
        Assert.Equal(original, Plan(Args()).Fingerprint);
        _creator.Reports[0] = Report(Existing, "replacement");
        Assert.NotEqual(original, Plan(Args()).Fingerprint);
        _creator.Reports[0] = Report(Existing, "old");
        _reader.Pages.Add(new() { Name = "=GAA+A1&EFS1/2" });
        Assert.NotEqual(original, Plan(Args()).Fingerprint);
    }

    [Fact]
    public void Execute_ReturnsMatchingPagesAndNewNamesAndReadsAgainAfterCommit()
    {
        _creator.After = [Report(Existing, "replacement"), Report(First, "new")];
        ReportCreateData data = Execute(Prepared());
        Assert.Equal(2, data.Pages.Count);
        Assert.Equal([Existing, First], data.Pages);
        Assert.Equal([First], data.NewPages);
        Assert.True(_creator.ReadAfterCommit);
        Assert.Equal(["Begin AI: eplan_report_create", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_IncludesUnchangedReportsWithTheSameTypeAndForm()
    {
        _creator.After = [.. _creator.Reports, Report(First, "new")];
        ReportCreateData data = Execute(Prepared());
        Assert.Equal([Existing, First], data.Pages);
        Assert.Equal([First], data.NewPages);
    }

    [Fact]
    public void Execute_ExistingMatchingPagesWithoutNewNamesCommitAsOverwrite()
    {
        _creator.After = [.. _creator.Reports];
        ReportCreateData data = Execute(Prepared());
        Assert.Equal([Existing], data.Pages);
        Assert.Empty(data.NewPages);
        Assert.Contains("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_NewNamesUseAllExistingPagesAndMatchingPagesExcludeOtherReports()
    {
        ReportPageRecord other = Report("=GAA+A1&EFS1/1", "circuit-now-report");
        other.Type = "TerminalDiagram";
        _creator.After = [Report(First, "new"), other];
        ReportCreateData data = Execute(Prepared());
        Assert.Equal([First], data.Pages);
        Assert.Equal([First], data.NewPages);
    }

    [Fact]
    public void Execute_NewReportWithUnreadableFieldsIsNotAssumedToBeNoGeneration()
    {
        ReportPageRecord report = Report(First, "new");
        report.Form = string.Empty;
        _creator.After = [report];
        ReportCreateData data = Execute(Prepared());
        Assert.Empty(data.Pages);
        Assert.Equal([First], data.NewPages);
        Assert.Contains("Commit", _transactions.Calls);
    }

    [Theory]
    [InlineData(ErrorReason.InvalidArgument)]
    [InlineData(ErrorReason.EplanOperationFailed)]
    public void Execute_PreservesTheNativeErrorAndRollsBack(string reason)
    {
        _creator.Failure = new BridgeErrorException(reason, "Choose an existing scheme.")
        { Error = { Metadata = new() { ["eplanMessage"] = "Scheme 'missing' is invalid." } } };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Prepared())).Error;
        Assert.Equal((reason, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal("Scheme 'missing' is invalid.", error.Metadata!["eplanMessage"]);
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_NoGeneratedPages_RollsBackAndRestoresTheInventory()
    {
        _creator.After = [];
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Prepared())).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal([Existing], _creator.Reports.Select(p => p.Name));
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_CommittedReadbackMismatch_RollsBackAndVerifiesRestoration()
    {
        _creator.After = [Report(Existing, "replacement")];
        _creator.WrongFormAfterCommit = true;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Prepared())).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.True(_creator.ReadAfterCommit);
        Assert.Equal([Existing], _creator.Reports.Select(p => p.Name));
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_MissingOrStaleFingerprint_NeverStartsATransaction()
    {
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Execute(Args())).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (missing.Reason, missing.Execution));
        ReportCreateArgs args = Prepared();
        _creator.Reports[0] = Report(Existing, "replaced");
        BridgeError stale = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (stale.Reason, stale.Execution));
        Assert.Empty(_creator.Creates);
        Assert.Empty(_transactions.Calls);
    }

    private static ReportCreateArgs Args() => new() { Project = "Line1", Type = "TableOfContents", Form = "F06_002", FirstPage = First };
    private static ReportPageRecord Report(string name, string id) => new() { Name = name, Id = id, Type = "TableOfContents", Form = "F06_002" };
    private WritePlan Plan(ReportCreateArgs args) => (WritePlan)new ReportCreatePlanOperation(new FakeCatalog(_project), _reader, _creator).Execute(args)!;
    private ReportCreateData Execute(ReportCreateArgs args) => (ReportCreateData)new ReportCreateExecuteOperation(new FakeCatalog(_project), _reader, _creator, _transactions).Execute(args)!;
    private ReportCreateArgs Prepared()
    {
        ReportCreateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        return args;
    }

    private sealed class Creator(FakePageReader reader, FakeTransactions transactions) : IReportCreator
    {
        public List<ReportPageRecord> Reports { get; set; } = [];
        public List<ReportPageRecord> After { get; set; } = [];
        public List<ReportCreateArgs> Creates { get; } = [];
        public bool WrongFormAfterCommit { get; set; }
        public bool ReadAfterCommit { get; private set; }
        public BridgeErrorException? Failure { get; set; }
        public IReadOnlyList<ReportPageRecord> ReportPages(string projectPath)
        {
            bool committed = transactions.Calls.Contains("Commit", StringComparer.Ordinal);
            ReadAfterCommit |= committed;
            return [.. Reports.Select(p => new ReportPageRecord
            {
                Name = p.Name, Id = p.Id, Type = p.Type,
                Form = committed && WrongFormAfterCommit && p.Id != "old" ? "WrongForm" : p.Form,
            })];
        }
        public void Create(string projectPath, ReportCreateArgs args)
        {
            Creates.Add(args);
            if (Failure is not null)
            {
                throw Failure;
            }
            Reports = [.. After];
            foreach (ReportPageRecord report in Reports.Where(r => !reader.Pages.Any(p => p.Name == r.Name)))
            {
                reader.Pages.Add(new() { Name = report.Name, Type = report.Type });
            }
        }
    }
}
