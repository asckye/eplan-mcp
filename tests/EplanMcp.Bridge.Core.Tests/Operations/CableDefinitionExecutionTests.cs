using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class CableDefinitionExecutionTests
{
    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 8)]
    public void Success_StagesCommitInOrder_ReportsActualCoresAndUndoSteps(bool sameCall, int steps)
    {
        using var fake = new Creator(sameCall);
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(steps, (int)result["undoSteps"]!);
        Assert.Equal(steps, fake.Commits);
        Assert.Equal(["definitions", "generate", "bind", "generate", "cores", "generate", "readback"], fake.Events.TakeLast(7));
        Assert.Equal("+A1-W3", (string?)result["actualCores"]![0]!["designation"]);
        Assert.Equal("BK", (string?)result["actualCores"]![0]!["colour"]);
        Assert.Equal(steps.ToString(System.Globalization.CultureInfo.InvariantCulture), File.ReadAllText(fake.FilePath));
        Assert.DoesNotContain("undo", fake.Events);
        Assert.Null(result["_cableBefore"]);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("colour")]
    [InlineData("type")]
    [InlineData("designation")]
    [InlineData("pins")]
    [InlineData("oid")]
    [InlineData("definitions")]
    [InlineData("bind")]
    [InlineData("cores")]
    [InlineData("generate")]
    [InlineData("cancel")]
    public void Failure_RollsBackCurrentStage_UndoesOnlyThisCallsCommitsAndChecksOriginalFingerprint(string failure)
    {
        foreach (bool sameCall in new[] { false, true })
        {
            using var fake = new Creator(sameCall) { Failure = failure };
            BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
            Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
            Assert.Equal("0", File.ReadAllText(fake.FilePath));
            Assert.Equal(fake.Commits, fake.Events.Count(e => e == "undo"));
            Assert.False(fake.InTransaction);
            Assert.True(fake.RestorationRead);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 5)]
    [InlineData(4, 7)]
    public void AnyGenerationFailure_AbortsCurrentStageAndUndoesEveryEarlierCommit(int generation, int commits)
    {
        using var fake = new Creator(true) { FailureGenerate = generation };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(commits, fake.Commits);
        Assert.Equal(commits, fake.Events.Count(e => e == "undo"));
        Assert.Equal("0", File.ReadAllText(fake.FilePath));
        Assert.DoesNotContain("readback", fake.Events);
        Assert.False(fake.InTransaction);
        Assert.True(fake.RestorationRead);
    }

    [Theory]
    [InlineData("undo")]
    [InlineData("restore")]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("close")]
    public void CompensationOrTransactionUncertain_ReportsUnknown(string failure)
    {
        using var fake = new Creator(false) { Failure = failure };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        if (failure is "commit" or "rollback" or "close")
        { Assert.DoesNotContain("undo", fake.Events); }
    }

    [Theory]
    [InlineData("4")]
    [InlineData("0")]
    [InlineData("duplicate")]
    public void InvalidAssignments_RefusedBeforePreparingOrStartingTransactions(string label)
    {
        using var fake = new Creator(false);
        if (label == "duplicate")
        { fake.Fixture.Cores.Add(fake.Fixture.Cores[0]!.DeepClone()); }
        else
        { fake.Fixture.Cores[0]!["label"] = label; }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(fake.Events);
        Assert.Equal(0, fake.Commits);
        Assert.Equal("0", File.ReadAllText(fake.FilePath));
    }

    private sealed class Creator : IConnectionCreator, IConnectionGenerator, ICableDefinitionWriter, ITransactionScopeFactory, IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("cable-execute-").FullName;
        private readonly Stack<int> _history = new();
        private int _state;
        private int _generates;
        public CableDefinitionFixture Fixture { get; } = new();
        public string FilePath => Path.Combine(_root, "state");
        public List<string> Events { get; } = [];
        public string? Failure { get; init; }
        public int FailureGenerate { get; init; }
        public int Commits { get; private set; }
        public bool InTransaction { get; private set; }
        public bool RestorationRead { get; private set; }
        public Creator(bool sameCall)
        {
            if (!sameCall)
            {
                var wire = Fixture.AddExisting();
                Fixture.Cores[0]!["connection"] = CableDefinitionFixture.Reference(wire);
                Fixture.Cable["points"] = JArray.Parse("[[32,16],[48,16]]");
                Fixture.Base.Request["items"] = new JArray(Fixture.Cable);
            }
            Save(0);
        }
        public JObject Execute() => Assert.IsType<JObject>(new OperationContext("cable", true, null).Run(() =>
            new ConnectionCreateExecuteOperation(this, this, this).Execute(new JObject { ["request"] = Fixture.Base.Request, ["fingerprint"] = Fixture.Plan()["fingerprint"] })));
        private void Save(int state) { _state = state; File.WriteAllText(FilePath, state.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        private void Stage(string stage)
        {
            Assert.True(InTransaction);
            Events.Add(stage);
            Save(_state + 1);
            if (Failure == stage || Failure == "rollback" && stage == "cores")
            { throw Failed(); }
            if (Failure == "cancel" && stage == "cores")
            { OperationContext.Current.Cancel(); }
        }
        public ConnectionCreatePlanner.Scene Read(string project, string page)
        {
            if (Events.Contains("undo", StringComparer.Ordinal) || Events.Contains("definitions", StringComparer.Ordinal))
            { RestorationRead = true; }
            if (_state > 0 && Fixture.Base.Request["items"]!.Count() > 1)
            { return new() { Graphical = Fixture.Plan()["graphicalAutoconnect"]!.ToObject<List<double[][]>>()! }; }
            if (Failure == "restore" && RestorationRead)
            { Fixture.Base.Scene.Page.GridSize = 2; }
            return Fixture.Base.Scene;
        }
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => Fixture.Base.Read(projectPath, args);
        public Action Prepare(string project, string page, JArray symbols) => () => Stage("symbols");
        public Action PrepareCables(string project, string page, JObject request, JObject plan) => () => Stage("definitions");
        public void BindCables(string project, string page, JObject request, JObject plan) { Assert.True(_generates > 0); Stage("bind"); }
        public void AssignCores(string project, string page, JObject request, JObject plan) { Assert.True(_generates >= 2); Stage("cores"); }
        public JArray ReadCores(string project, string page, JObject request, JObject plan)
        {
            Assert.False(InTransaction);
            Events.Add("readback");
            var row = new JObject { ["designation"] = "+A1-W3", ["type"] = Fixture.Cable["type_text"]!.DeepClone(), ["label"] = "1", ["colour"] = "BK", ["oid"] = 9, ["pins"] = new JArray("17/3/0", "17/4/0") };
            if (Failure is "label" or "colour" or "type" or "designation")
            { row[Failure] = "wrong"; }
            if (request["items"]!.Count() > 1)
            { row["pins"] = new JArray("17/1/0", "17/2/0"); }
            if (Failure == "pins")
            { row["pins"] = new JArray("17/8/0", "17/9/0"); }
            if (Failure == "oid")
            { row["oid"] = request["items"]!.Count() > 1 ? 0 : 100; }
            if (Failure is "undo" or "restore")
            { row["label"] = "wrong"; }
            return new JArray(row);
        }
        public List<string?[]> Endpoints(string project, string page) => Fixture.Base.Request["items"]!.Count() == 1 ? [["17/3/0", "17/4/0"]] : [["17/1/0", "17/2/0"]];
        public void Generate(string projectPath, IReadOnlyList<string> pages)
        {
            _generates++;
            Stage("generate");
            if (_generates == FailureGenerate)
            { throw Failed(); }
        }
        public void Undo(string project)
        {
            Assert.False(InTransaction);
            Events.Add("undo");
            if (Failure == "undo")
            { throw Failed(); }
            Save(_history.Pop());
        }
        public ITransactionScopeFactory PlacementTransactions(string project) => this;
        public ITransactionScope Begin(string undoDescription) { Assert.False(InTransaction); InTransaction = true; return new Scope(this, _state); }
        private sealed class Scope(Creator owner, int before) : ITransactionScope
        {
            public void Commit() { if (owner.Failure == "commit") { throw Failed(); } owner.Commits++; owner._history.Push(before); }
            public void Rollback() { if (owner.Failure == "rollback") { throw Failed(); } owner.Save(before); }
            public void CloseUndo() { if (owner.Failure == "close") { throw Failed(); } }
            public void Dispose() => owner.InTransaction = false;
        }
        public JObject Interruptions(string project, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages) => throw new NotSupportedException();
        public void Dispose() => Directory.Delete(_root, true);
        private static BridgeErrorException Failed() => new(ErrorReason.EplanOperationFailed, "Injected cable stage failure.");
    }
}
