using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>Memory-only generation, backup boundary and rollback; the actual backup checks have their own Medium tests.</summary>
[Trait("Size", "Small")]
public sealed class CableGenerateOperationTests
{
    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };
    private readonly FakeGenerator _generator = new();
    private readonly FakeTransactions _transactions = new();
    private readonly List<string> _steps = [];
    private Action? _onBackup;

    public CableGenerateOperationTests()
    {
        _generator.Snapshot = Snapshot(Cable("auto", true), Cable("manual", false), Cable("unknown", null));
        _transactions.Snapshot = () =>
        {
            Assert.Equal(["backup"], _steps);
            CableGenerationSnapshot before = _generator.Snapshot;
            return () => _generator.Snapshot = before;
        };
    }

    [Fact]
    public void Plan_RequiresAllThreeSchemesAndProject_ListsEveryViolation()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new CableGenerateArgs())).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(4, error.Metadata!["violations"].Split('\n').Length);
        Assert.Empty(_generator.Calls);
    }

    [Fact]
    public void Plan_UnknownSchemes_OffersAllThreeCandidateListsWithLimitsAndNoInternalScheme()
    {
        _generator.Choices = [SchemeChoice.Internal, .. Enumerable.Range(1, 25).Select(n => "Scheme" + n.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;
        var candidates = JObject.Parse(error.Metadata!["candidates"]);

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(3, error.Metadata["violations"].Split('\n').Length);
        Assert.Equal(["creationScheme", "numberingScheme", "selectionScheme"], candidates.Properties().Select(p => p.Name));
        Assert.All(candidates.Properties(), p =>
        {
            Assert.Equal(20, ((JArray)p.Value).Count);
            Assert.DoesNotContain(SchemeChoice.Internal, p.Value.Values<string>());
        });
    }

    [Fact]
    public void Plan_NoAutomaticCables_StillPlansOneUpdateAndDoesNotGenerate()
    {
        _generator.Snapshot = Snapshot([.. Enumerable.Range(0, 60).Select(n => Cable(n.ToString(System.Globalization.CultureInfo.InvariantCulture), null))]);
        WritePlan plan = Plan(Args());

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "cables", "Line1", null), (change.Action, change.Kind, change.Name, change.NewValue));
        Assert.Equal(50, JArray.Parse(plan.Details!["cables"]).Count);
        Assert.Equal(("true", "true", "1", "1"), (plan.Details["keepOldNames"], plan.Details["onlyAutoCables"], plan.Details["start"], plan.Details["step"]));
        Assert.Equal(60, JObject.Parse(plan.Details["before"])["automaticUnknown"]!.Value<int>());
        Assert.Empty(_generator.Calls);
        Assert.Empty(_steps);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("connections")]
    [InlineData("business")]
    [InlineData("flag")]
    [InlineData("name")]
    [InlineData("identity")]
    public void Execute_FingerprintChangeBeforeBackup_IsStaleAndDoesNotGenerate(string field)
    {
        CableGenerateArgs args = Planned();
        CableGenerationCable cable = _generator.Snapshot.Cables[0];
        _generator.Snapshot = field switch
        {
            "state" => new(_generator.Snapshot.Cables, 4, "same-name scheme content changed"),
            "connections" => new(_generator.Snapshot.Cables, 5, "scheme and endpoints"),
            "business" => Snapshot(Cable(cable.Id, true, "changed")),
            "flag" => Snapshot(Cable(cable.Id, null)),
            "name" => Snapshot(new CableGenerationCable(cable.Id, "+A1-W9", true, cable.Business)),
            _ => Snapshot(Cable("replacement", true)),
        };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_steps);
        Assert.Empty(_transactions.Calls);
        Assert.Empty(_generator.Calls);
    }

    [Fact]
    public void Execute_FingerprintChangeDuringBackup_IsStaleBeforeTransaction()
    {
        CableGenerateArgs args = Planned();
        _onBackup = () => _generator.Snapshot = new(_generator.Snapshot.Cables, 4, "scheme changed during backup");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_generator.Calls);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData("renamed")]
    [InlineData("removed")]
    [InlineData("replaced")]
    [InlineData("manualBusiness")]
    [InlineData("unknownBusiness")]
    [InlineData("unknownFlag")]
    [InlineData("newUnknown")]
    [InlineData("newManual")]
    [InlineData("autoFlagLost")]
    public void Execute_UnsafeReadback_RestoresBaselineAndReportsFailure(string change)
    {
        CableGenerateArgs args = Planned();
        CableGenerationSnapshot before = _generator.Snapshot;
        _generator.OnGenerate = () => _generator.Snapshot = Snapshot(change switch
        {
            "renamed" => [new("auto", "+A1-W9", true, "original"), Cable("manual", false), Cable("unknown", null)],
            "removed" => [Cable("manual", false), Cable("unknown", null)],
            "replaced" => [Cable("replacement", true), Cable("manual", false), Cable("unknown", null)],
            "manualBusiness" => [Cable("auto", true), Cable("manual", false, "part changed"), Cable("unknown", null)],
            "unknownBusiness" => [Cable("auto", true), Cable("manual", false), Cable("unknown", null, "core changed")],
            "unknownFlag" => [Cable("auto", true), Cable("manual", false), Cable("unknown", false)],
            "autoFlagLost" => [Cable("auto", null), Cable("manual", false), Cable("unknown", null)],
            _ => [.. before.Cables, Cable("new", change == "newManual" ? false : null)],
        });

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Same(before, _generator.Snapshot);
        Assert.Equal(["Begin AI: eplan_cable_generate", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_Success_CountsNullableFlagsAndChangesByIdentityBeforeTruncation()
    {
        CableGenerateArgs args = Planned();
        _generator.OnGenerate = () => _generator.Snapshot = new(
            [Cable("auto", true, "part changed"), Cable("manual", false), Cable("unknown", null),
                .. Enumerable.Range(0, 205).Select(n => new CableGenerationCable("new" + n.ToString(System.Globalization.CultureInfo.InvariantCulture), "same name", true, "new"))], 9, "after");

        CableGenerateData result = Execute(args);

        Assert.Equal((3, 1, 1, 4), (result.Before.Cables, result.Before.Automatic, result.Before.AutomaticUnknown, result.Before.Connections));
        Assert.Equal((208, 206, 1, 9), (result.After.Cables, result.After.Automatic, result.After.AutomaticUnknown, result.After.Connections));
        Assert.Equal((206, 200, true), (result.ChangedCount, result.Changes.Count, result.Truncated));
        Assert.Equal(("updated", "+A1-W1"), (result.Changes[0].Kind, result.Changes[0].Name));
        Assert.All(result.Changes.Skip(1), c => Assert.Equal(("created", "same name"), (c.Kind, c.Name)));
        Assert.Equal([(_project.Path, "Standard", "Standard", "Standard")], _generator.Calls);
        Assert.Equal(["Begin AI: eplan_cable_generate", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Contains("do not repeat", result.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NoObservableChanges_CommitsWithReadbackLimitAndDoesNotPromiseUndo()
    {
        CableGenerateData result = Execute(Planned());

        Assert.Equal(0, result.ChangedCount);
        Assert.Empty(result.Changes);
        Assert.False(result.Truncated);
        Assert.Contains("Large verification", result.Hint, StringComparison.Ordinal);
        Assert.Contains("Commit", _transactions.Calls);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("backup")]
    [InlineData("action")]
    public void Execute_Cancellation_StopsAtSafePointAndRollsBackWhenStarted(string when)
    {
        CableGenerateArgs args = Planned();
        var context = new OperationContext("cancel", write: true, progress: null);
        if (when == "before")
        {
            context.Cancel();
        }
        else if (when == "backup")
        {
            _onBackup = context.Cancel;
        }
        else
        {
            _generator.OnGenerate = () =>
            {
                _generator.Snapshot = Snapshot(Cable("new", true));
                context.Cancel();
            };
        }

        CableGenerationSnapshot baseline = _generator.Snapshot;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => Execute(args))).Error;

        Assert.Equal(ErrorReason.Cancelled, error.Reason);
        Assert.Equal(when == "action" ? ExecutionState.RolledBack : ExecutionState.NotStarted, error.Execution);
        Assert.Same(baseline, _generator.Snapshot);
        Assert.DoesNotContain("Commit", _transactions.Calls);
        Assert.Equal(when == "action" ? 1 : 0, _generator.Calls.Count);
    }

    [Fact]
    public void Execute_BackupFailure_NeverBeginsOrGenerates()
    {
        _onBackup = () => throw new BridgeErrorException(ErrorReason.BackupFailed, "Backup failed.");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Planned())).Error;

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
        Assert.Empty(_generator.Calls);
    }

    [Fact]
    public void Execute_ActionFailureAndRollbackFailure_ReportsUnknownOutcome()
    {
        _generator.OnGenerate = () => throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Action returned false.");
        _transactions.FailAt = "Rollback";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Planned())).Error;

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_ActionOrRequiredReadbackFails_RollsBack(bool readback)
    {
        CableGenerateArgs args = Planned();
        CableGenerationSnapshot baseline = _generator.Snapshot;
        _generator.OnGenerate = () =>
        {
            _generator.Snapshot = Snapshot(Cable("new", true));
            if (!readback)
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Action failed.");
            }

            _generator.FailRead = true;
        };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Same(baseline, _generator.Snapshot);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void Execute_AfterCommitFailure_IsASuccessWarning()
    {
        _transactions.FailAt = "CloseUndo";

        CableGenerateData result = Execute(Planned());

        Assert.Contains("committed", result.Warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Fingerprint_OrderIndependentAndBoundToSelectedSchemes()
    {
        CableGenerateArgs args = Args();
        string first = Plan(args).Fingerprint;
        _generator.Snapshot = new([.. _generator.Snapshot.Cables.Reverse()], 4, "scheme and endpoints");
        Assert.Equal(first, Plan(args).Fingerprint);
        _generator.Choices = ["Standard", "Other"];
        args.SelectionScheme = "Other";
        Assert.NotEqual(first, Plan(args).Fingerprint);
    }

    private static CableGenerateArgs Args() => new() { Project = "Line1", CreationScheme = "Standard", NumberingScheme = "Standard", SelectionScheme = "Standard" };

    private static CableGenerationCable Cable(string id, bool? automatic, string business = "original") => new(id, "+A1-W1", automatic, business);

    private static CableGenerationSnapshot Snapshot(params CableGenerationCable[] cables) => new(cables, 4, "scheme and endpoints");

    private WritePlan Plan(CableGenerateArgs args) => (WritePlan)new CableGeneratePlanOperation(new FakeCatalog(_project), _generator).Execute(args)!;

    private CableGenerateArgs Planned()
    {
        CableGenerateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        return args;
    }

    private CableGenerateData Execute(CableGenerateArgs args) => (CableGenerateData)new CableGenerateExecuteOperation(new FakeCatalog(_project), new FakePageReader(), _generator, new FakeProjectBackup(), _transactions)
    {
        MakeBackup = (_, _) =>
        {
            _steps.Add("backup");
            _onBackup?.Invoke();
        },
    }.Execute(args)!;

    private sealed class FakeGenerator : ICableGenerator
    {
        public CableGenerationSnapshot Snapshot { get; set; } = new([], 0, "empty");
        public IReadOnlyList<string> Choices { get; set; } = ["Standard", SchemeChoice.Internal];
        public Action? OnGenerate { get; set; }
        public bool FailRead { get; set; }
        public List<(string Path, string Creation, string Numbering, string Selection)> Calls { get; } = [];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Schemes(string projectPath) => new Dictionary<string, IReadOnlyList<string>>
        {
            ["creationScheme"] = Choices,
            ["numberingScheme"] = Choices,
            ["selectionScheme"] = Choices,
        };

        public CableGenerationSnapshot Read(string projectPath) => FailRead ? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Required readback failed.") : Snapshot;

        public void Generate(string projectPath, string creationScheme, string numberingScheme, string selectionScheme)
        {
            Calls.Add((projectPath, creationScheme, numberingScheme, selectionScheme));
            OnGenerate?.Invoke();
        }
    }
}
