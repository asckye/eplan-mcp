using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionCreateExecutionTests
{
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
            return new() { Graphical = Graphical(project, page) };
        }
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => Fixture.Read(projectPath, args);
        public Action Prepare(string project, string page, JArray symbols)
        {
            if (Failure == "prepare")
            { throw Failed(); }
            Assert.Empty(Transactions.Calls);
            return () => { Events.Add("place"); State = 1; if (Failure == "placement") { throw Failed(); } };
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
        public ITransactionScopeFactory PlacementTransactions(string project) => Transactions;
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        private static BridgeErrorException Failed() => new(ErrorReason.EplanOperationFailed, "Injected failure.");
    }
}
