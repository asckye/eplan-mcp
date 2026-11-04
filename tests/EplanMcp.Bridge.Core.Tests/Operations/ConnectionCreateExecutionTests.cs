using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionCreateExecutionTests
{
    private static void Measure(Creator fake, double[] bounds)
    {
        fake.Measured = LayoutGuard.From(fake.Fixture.Scene);
        fake.Measured.Page = new()
        {
            GridSize = 4,
            PageSize = [100, 100],
            Placements = [.. fake.Fixture.Scene.Page.Placements, new()
            {
                Kind = "SymbolReference", Locator = new() { Type = 17, Oid = 9 }, Location = [24, 8], Bounds = bounds
            }]
        };
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MeasuredConflictAfterEitherCommit_CommitsWithADeviationWarning(int commit)
    {
        // ADR-0046: a measured overlap of a planned object that stays on the page is a committed deviation.
        var fake = new Creator { MeasureAfterCommit = commit };
        Measure(fake, [20, 6, 26, 26]);

        JObject result = fake.Execute();

        Assert.Contains("Readback deviation: R2", (string?)result["warning"], StringComparison.Ordinal);
        Assert.Contains("Objects 17/9", (string?)result["warning"], StringComparison.Ordinal);
        Assert.DoesNotContain("Rollback", fake.Transactions.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MeasuredOutlineOffThePageAfterEitherCommit_RollsBackItsOpenStepAndPreservesRefusal(int commit)
    {
        var fake = new Creator { MeasureAfterCommit = commit };
        Measure(fake, [20, -2, 26, 26]);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Contains("S2", error.Message, StringComparison.Ordinal);
        Assert.Contains("Geometry", error.Metadata!["layoutViolations"], StringComparison.Ordinal);
        Assert.Equal(0, fake.State);
        Assert.Equal(commit, fake.Transactions.Calls.Count(c => c == "Commit"));
        Assert.Equal(commit - 1, fake.Transactions.Calls.Count(c => c == "CloseUndo"));
        Assert.Contains("Rollback", fake.Transactions.Calls);
        Assert.Equal(commit - 1, fake.Events.Count(e => e == "undo"));
    }

    [Fact]
    public void UnknownPrediction_MeasuredOverlap_CommitsAndGeneratesWithAFix()
    {
        var fake = new Creator();
        fake.Fixture.Scene.SymbolBounds.Clear();
        Measure(fake, [20, 6, 26, 26]);
        JObject result = fake.Execute();
        Assert.Contains("Readback deviation: R2", (string?)result["warning"], StringComparison.Ordinal);
        Assert.Contains("adjust with eplan_placement_update", (string?)result["warning"], StringComparison.Ordinal);
        Assert.DoesNotContain("Suggested position", (string?)result["warning"], StringComparison.Ordinal);
        Assert.Contains("generate", fake.Events);
        Assert.DoesNotContain("Rollback", fake.Transactions.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Split_ExactReplacementGraphicsAndPreservedConnection_Commits(bool branchPin)
    {
        Creator fake = Split(branchPin);
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(branchPin ? 1 : 0, result["logicalAfterGenerate"]!.Count());
        Assert.Contains(result["actualEndpoints"]!, p => (string?)p[0] == "17/1/0" && (string?)p[1] == "17/2/0");
        Assert.Equal(2, fake.Transactions.Calls.Count(c => c == "Commit"));
        Assert.DoesNotContain("undo", fake.Events);
    }

    [Theory]
    [InlineData("unrelated_graphics", "autoconnect lines after placement")]
    [InlineData("unsplit_graphics", "autoconnect lines after placement")]
    [InlineData("lost_connection", "existing connection 17/1/0 - 17/2/0 is gone")]
    [InlineData("missing_branch", "were not connected")]
    [InlineData("unexpected_connection", "unplanned connection")]
    public void Split_GraphicsOrLogicalMismatch_StillRollsBack(string failure, string reason)
    {
        Creator fake = Split(failure == "missing_branch");
        switch (failure)
        {
            case "unrelated_graphics":
                fake.Drawn![3] = [[64, 64], [84, 64]];
                break;
            case "unsplit_graphics":
                fake.Drawn!.Add([[16, 8], [16, 40]]);
                break;
            case "lost_connection":
                fake.GeneratedEndpoints = [];
                break;
            case "missing_branch":
                fake.GeneratedEndpoints = [["17/1/0", "17/2/0"]];
                break;
            case "unexpected_connection":
                fake.GeneratedEndpoints = [["17/1/0", "17/2/0"], ["17/2/0", "17/77/0"]];
                break;
        }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, fake.State);
        Assert.Equal("undo", fake.Events[^1]);
        if (failure is "unrelated_graphics" or "unsplit_graphics")
        { Assert.DoesNotContain("generate", fake.Events); }
    }

    private static Creator Split(bool branchPin)
    {
        var fake = new Creator();
        fake.Fixture.SplitLine(branchPin: branchPin);
        fake.Fixture.Scene.Graphical.Add([[64, 64], [80, 64]]);
        fake.Drawn = [[[16, 24], [16, 8]], [[16, 40], [16, 24]], [[16, 24], [32, 24]], [[80, 64], [64, 64]]];
        fake.GeneratedEndpoints = [["17/1/0", "17/2/0"]];
        if (branchPin)
        {
            fake.Drawn.Add([[32, 24], [32, 8]]);
            fake.GeneratedEndpoints.Add(["17/2/0", "17/3/0"]);
        }
        return fake;
    }

    [Fact]
    public void Success_CommitsPlacementBeforeGeneration_ReadsActualEndpoints()
    {
        var fake = new Creator();
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(2, (int)result["undoSteps"]!);
        Assert.Equal(["place", "generate"], fake.Events);
        Assert.Equal(2, fake.Transactions.Calls.Count(c => c == "Commit"));
        Assert.Equal("17/1/0", (string?)result["actualEndpoints"]![0]![0]);
        Assert.DoesNotContain("undo", fake.Events);
    }

    [Theory]
    [InlineData("corner", "Left,Up")]
    [InlineData("t_node", "Left,Right,Up")]
    [InlineData("potential_point", "Up")]
    public void NodesWithoutLogicalEndpoints_CommitOneStepWithoutGeneration(string kind, string directions)
    {
        var fake = new Creator { GeneratedEndpoints = [] };
        fake.Fixture.Scene.Pins.Clear();
        fake.Fixture.Scene.Page.Placements.Clear();
        fake.Fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node(kind, [40, 40], directions.Split(',')));

        JObject result = fake.Execute();

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Single(result["symbols"]!);
        Assert.Empty(result["logicalAfterGenerate"]!);
        Assert.Empty(result["actualEndpoints"]!);
        Assert.Equal(["place"], fake.Events);
        Assert.Equal(1, fake.Transactions.Calls.Count(c => c == "Commit"));
    }

    [Fact]
    public void NodesWithoutEndpoints_GraphicalMismatch_UndoesPlacement()
    {
        var fake = new Creator { Drawn = [[[40, 40], [40, 48]]] };
        fake.Fixture.Scene.Pins.Clear();
        fake.Fixture.Scene.Page.Placements.Clear();
        fake.Fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("corner", [40, 40], "Left", "Up"));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;

        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(["place", "undo"], fake.Events);
        Assert.Equal(0, fake.State);
    }

    [Theory]
    [InlineData("preserved")]
    [InlineData("missing")]
    [InlineData("added")]
    [InlineData("duplicate")]
    public void IsolatedNode_PreservesExistingLogicalConnectionsWithoutGenerating(string outcome)
    {
        var fake = new Creator
        {
            GeneratedEndpoints = outcome switch
            {
                "missing" => [],
                "added" => [["17/7/0", "17/8/0"], ["17/9/0", "17/10/0"]],
                "duplicate" => [["17/7/0", "17/8/0"], ["17/7/0", "17/8/0"]],
                _ => [["17/7/0", "17/8/0"]]
            }
        };
        fake.Fixture.Scene.Pins.Clear();
        fake.Fixture.Scene.Page.Placements.Clear();
        fake.Fixture.Scene.Logical.Add(new() { StartDevice = "17/7/0", EndDevice = "17/8/0" });
        fake.Fixture.Request["items"] = new JArray(ConnectionCreateFixture.Node("corner", [40, 40], "Left", "Up"));
        if (outcome == "preserved")
        {
            JObject result = fake.Execute();
            Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
            Assert.Single(result["actualEndpoints"]!);
            Assert.Equal(["place"], fake.Events);
        }
        else
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
            Assert.Equal(ExecutionState.RolledBack, error.Execution);
            Assert.Equal(["place", "undo"], fake.Events);
            Assert.Equal(0, fake.State);
        }
        Assert.DoesNotContain("generate", fake.Events);
    }

    [Fact]
    public void IsolatedNodeWithAnExplicitWire_StillGeneratesWithTwoUndoSteps()
    {
        var fake = new Creator();
        ((JArray)fake.Fixture.Request["items"]!).Add(ConnectionCreateFixture.Node("corner", [80, 80], "Left", "Up"));
        JObject result = fake.Execute();
        Assert.Equal(2, (int)result["undoSteps"]!);
        Assert.Equal(["place", "generate"], fake.Events);
        Assert.Single(result["actualEndpoints"]!);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("endpoints")]
    [InlineData("graphical")]
    [InlineData("cancel")]
    public void FailedSecondStep_RollsBackBeforeCompensatingPlacement(string failure)
    {
        var fake = new Creator { Failure = failure };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(0, fake.State);
        Assert.Equal("undo", fake.Events[^1]);
        Assert.Equal(1, fake.Transactions.Calls.Count(c => c == "Commit"));
        if (failure != "graphical")
        { Assert.Contains("Rollback", fake.Transactions.Calls); }
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("undo")]
    [InlineData("restore")]
    [InlineData("rollback")]
    public void UncertainCommitOrCompensation_ReportsUnknown(string failure)
    {
        var fake = new Creator { Failure = failure };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Fact]
    public void StaleScene_DoesNotStartEitherStep()
    {
        var fake = new Creator();
        JObject request = fake.Arguments();
        fake.Fixture.Scene.Page.GridSize = 2;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Operation.Execute(request)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(fake.Transactions.Calls);
        Assert.Empty(fake.Events);
    }

    [Theory]
    [InlineData("placement")]
    [InlineData("prepare")]
    public void PlacementFailure_DoesNotGenerateOrUndoCommittedHistory(string failure)
    {
        var fake = new Creator { Failure = failure };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal(failure == "prepare" ? ExecutionState.NotStarted : ExecutionState.RolledBack, error.Execution);
        Assert.Equal(0, fake.State);
        Assert.DoesNotContain("generate", fake.Events);
        Assert.DoesNotContain("undo", fake.Events);
    }

    [Fact]
    public void Plan_ContainsCompleteApprovalEvidenceAndTwoUndoSteps()
    {
        var fake = new Creator();
        var operation = new ConnectionCreatePlanOperation(fake);
        WritePlan plan = Assert.IsType<WritePlan>(operation.Execute(fake.Fixture.Request));
        Assert.Contains("2 undo steps", plan.Changes[0].NewValue, StringComparison.Ordinal);
        Assert.Contains("logicalAfterGenerate", plan.Changes[0].NewValue, StringComparison.Ordinal);
        Assert.Empty(fake.Events);
    }

    private sealed class Creator : IConnectionCreator, IConnectionGenerator
    {
        public ConnectionCreateFixture Fixture { get; } = new();
        public FakeTransactions Transactions { get; } = new();
        public List<string> Events { get; } = [];
        public LayoutGuard.Model? Measured { get; set; }
        public int MeasureAfterCommit { get; init; }
        public string? Failure { get; init; }
        public List<double[][]>? Drawn { get; set; }
        public List<string?[]>? GeneratedEndpoints { get; set; }
        public int State { get; private set; }
        public ConnectionCreateExecuteOperation Operation => new(this, this, Transactions);
        public Creator()
        {
            Transactions.Snapshot = () =>
            {
                int before = State;
                return () => { State = before; };
            };
        }
        public JObject Arguments() => new() { ["request"] = Fixture.Request, ["fingerprint"] = Fixture.Plan()["fingerprint"] };
        public JObject Execute() => Assert.IsType<JObject>(new OperationContext("test", true, null).Run(() => Operation.Execute(Arguments())));
        public ConnectionCreatePlanner.Scene Read(string project, string page)
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (State == 0)
            { return Fixture.Scene; }
            return new() { Page = Fixture.Scene.Page, Layout = Fixture.Scene.Layout, Pins = Fixture.Scene.Pins, Graphical = Graphical(project, page) };
        }
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => Fixture.Read(projectPath, args);
        public LayoutGuard.Model Measure(string project, string page) => MeasureAfterCommit > 0 && Transactions.Calls.Count(c => c == "Commit") >= MeasureAfterCommit
            ? Measured! : LayoutGuard.From(Read(project, page));
        public Action Prepare(string project, string page, JArray symbols, Action<LayoutGuard.Model>? verifyLayout = null)
        {
            if (Failure == "prepare")
            { throw Failed(); }
            Assert.Empty(Transactions.Calls);
            return () => { Events.Add("place"); State = 1; if (Failure == "placement") { throw Failed(); } if (MeasureAfterCommit == 0 && Measured is { } measured) { verifyLayout?.Invoke(measured); } };
        }
        public List<double[][]> Graphical(string project, string page) => Failure == "graphical" ? [] : Drawn ?? Fixture.Plan()["graphicalAutoconnect"]!.ToObject<List<double[][]>>()!;
        public List<string?[]> Endpoints(string project, string page) => Failure == "endpoints" ? [["17/1/0", "17/9/0"]] : GeneratedEndpoints ?? [["17/1/0", "17/2/0"]];
        public void Generate(string projectPath, IReadOnlyList<string> pages)
        {
            Assert.Equal(1, State);
            Assert.Equal(1, Transactions.Calls.Count(c => c == "Commit"));
            Assert.Equal([ConnectionCreateFixture.PageName], pages);
            Events.Add("generate");
            State = 2;
            if (Failure == "commit")
            { Transactions.FailAt = "Commit"; }
            if (Failure == "rollback")
            { Transactions.FailAt = "Rollback"; throw Failed(); }
            if (Failure == "cancel")
            { OperationContext.Current.Cancel(); }
            if (Failure is "generation" or "undo" or "restore")
            { throw Failed(); }
        }
        public void Undo(string project)
        {
            Events.Add("undo");
            Assert.Equal(1, State);
            if (Failure == "undo")
            { throw Failed(); }
            State = 0;
            if (Failure == "restore")
            { Fixture.Scene.Page.GridSize = 2; }
        }
        public JObject Interruptions(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public ITransactionScopeFactory PlacementTransactions(string project, bool undoCommitted = false) => Transactions;
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        private static BridgeErrorException Failed() => new(ErrorReason.EplanOperationFailed, "Injected failure.");
    }
}
