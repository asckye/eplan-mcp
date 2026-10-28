using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ConnectionCreateExecutionTests
{
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
        public List<double[][]> Graphical(string project, string page) => Failure == "graphical" ? [] : Fixture.Plan()["graphicalAutoconnect"]!.ToObject<List<double[][]>>()!;
        public List<string?[]> Endpoints(string project, string page) => Failure == "endpoints" ? [["17/1/0", "17/9/0"]] : [["17/1/0", "17/2/0"]];
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
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        private static BridgeErrorException Failed() => new(ErrorReason.EplanOperationFailed, "Injected failure.");
    }
}
