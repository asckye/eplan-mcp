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
        Assert.Equal(["definitions", "definitions-readback", "generate", "bind", "bind-readback", "generate", "cores", "readback", "generate", "readback"], fake.Events.TakeLast(10));
        Assert.Equal("+A1-W3", (string?)result["actualCores"]![0]!["designation"]);
        Assert.Equal("BK", (string?)result["actualCores"]![0]!["colour"]);
        Assert.Equal(steps.ToString(System.Globalization.CultureInfo.InvariantCulture), File.ReadAllText(fake.FilePath));
        Assert.DoesNotContain("undo", fake.Events);
        Assert.Equal(0, fake.Aborts);
        Assert.Null(result["_cableBefore"]);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("colour")]
    [InlineData("type")]
    [InlineData("designation")]
    [InlineData("pins")]
    [InlineData("oid")]
    [InlineData("bind")]
    [InlineData("bind-readback")]
    [InlineData("generate")]
    public void Failure_RollsBackCurrentStage_UndoesOnlyThisCallsCommitsAndChecksRestoredContent(string failure)
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
    [InlineData("designation")]
    [InlineData("type")]
    [InlineData("label")]
    [InlineData("colour")]
    public void CoreReadbackMismatch_RolledBack_RetainsFieldAndActualValue(string field)
    {
        foreach (bool final in new[] { false, true })
        {
            using var fake = new Creator(false) { Failure = final ? "final-" + field : field };
            BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => fake.Execute());
            Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
            Assert.Contains("core " + field + " readback", error.InnerException!.Message, StringComparison.Ordinal);
            Assert.Contains("actual 'wrong'", error.InnerException.Message, StringComparison.Ordinal);
            Assert.Equal(final ? 6 : 5, fake.Commits);
            Assert.Equal(fake.Commits, fake.Events.Count(e => e == "undo"));
            Assert.Equal(0, fake.Aborts);
            Assert.Equal(0, fake.NativeObjects);
            Assert.Equal("0", File.ReadAllText(fake.FilePath));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefinitionReadbackMismatch_AfterCommit_UndoesCableAndPropertyPlacementsWithoutAbort(bool sameCall)
    {
        using var fake = new Creator(sameCall) { Failure = "definition-readback" };
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => fake.Execute());
        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
        Assert.Contains("type_text: expected 'cable'; actual '<empty>'", error.InnerException!.Message, StringComparison.Ordinal);
        Assert.Equal(sameCall ? 3 : 1, fake.Commits);
        Assert.Equal(fake.Commits, fake.Events.Count(e => e == "undo"));
        Assert.Equal(0, fake.Aborts);
        Assert.Equal(0, fake.NativeObjects);
        Assert.DoesNotContain(fake.Fixture.Base.Scene.Page.Placements, p => p.Kind is "Cable" or "PropertyPlacement");
        Assert.True(fake.RestorationRead);
        Assert.Equal("0", File.ReadAllText(fake.FilePath));
    }

    [Theory]
    [InlineData("definitions", 6)]
    [InlineData("cores", 7)]
    [InlineData("cancel", 7)]
    public void NativeCreationFailure_AbortLeavesObjects_ReportsUnknownWithoutReadingThem(string failure, int remaining)
    {
        using var fake = new Creator(false) { Failure = failure };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        Assert.Equal(1, fake.Aborts);
        Assert.Equal(remaining, fake.NativeObjects);
        Assert.False(fake.InTransaction);
        Assert.False(fake.RestorationRead);
        Assert.DoesNotContain("undo", fake.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undo_RenumbersIdentifiersAndRestoresContent_ReportsRolledBack(bool sameCall)
    {
        using var fake = new Creator(sameCall) { Failure = "label", RenumberOnUndo = true };
        fake.Fixture.Base.Scene.Page.Placements.Add(new() { Parent = fake.Fixture.Base.Scene.Page.Placements[0].Locator, Kind = "PropertyPlacement", Location = [5, 5], Bounds = [4, 4, 6, 6] });
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => fake.Execute());
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Equal(101, fake.Fixture.Base.Scene.Page.Placements[0].Locator.Oid);
        Assert.Equal(new string('f', 64), fake.Fixture.Base.Scene.Page.Placements[0].Locator.Digest);
        Assert.All(fake.Fixture.Inventory.Wires, w => Assert.Equal(109, w.Locator.Oid));
        Assert.Equal(fake.Commits, fake.Events.Count(e => e == "undo"));
        Assert.Equal("0", File.ReadAllText(fake.FilePath));
    }

    [Theory]
    [InlineData("page")]
    [InlineData("placements")]
    [InlineData("pins")]
    [InlineData("graphical")]
    [InlineData("endpoints")]
    [InlineData("cableNames")]
    [InlineData("cores")]
    public void Undo_ContentStillDiffers_ReportsUnknownWithExpectedAndActualDifference(string difference)
    {
        using var fake = new Creator(false) { Failure = "label", RestorationDifference = difference };
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => fake.Execute());
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Error.Reason, error.Error.Execution));
        var causes = Assert.IsType<AggregateException>(error.InnerException);
        string message = causes.InnerExceptions[1].Message;
        Assert.Contains("Restoration " + difference + " differs: expected ", message, StringComparison.Ordinal);
        Assert.Contains("; actual ", message, StringComparison.Ordinal);
        Assert.Equal(fake.Commits, fake.Events.Count(e => e == "undo"));
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
        Assert.Equal(generation == 4 ? 1 : 0, fake.Events.Count(e => e == "readback"));
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
        private readonly Stack<(int State, int Objects)> _history = new();
        private readonly List<double[][]> _graphical;
        private readonly List<DrawingPlacement> _createdPlacements = [];
        private bool _abortedCreation;
        private int _state;
        private int _generates;
        public CableDefinitionFixture Fixture { get; } = new();
        public string FilePath => Path.Combine(_root, "state");
        public List<string> Events { get; } = [];
        public string? Failure { get; init; }
        public int FailureGenerate { get; init; }
        public bool RenumberOnUndo { get; init; }
        public string? RestorationDifference { get; init; }
        public int Commits { get; private set; }
        public int Aborts { get; private set; }
        public int NativeObjects { get; private set; }
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
            _graphical = Fixture.Plan()["graphicalAutoconnect"]!.ToObject<List<double[][]>>()!;
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
            if (stage == "definitions")
            {
                NativeObjects = 6;
                var cable = new DrawingPlacement { Locator = new() { Type = 23, Oid = 10 }, Kind = "Cable", Location = [60, 140], Bounds = [59.75, 139.75, 220.25, 140.25] };
                _createdPlacements.Add(cable);
                for (int i = 0; i < 5; i++)
                { _createdPlacements.Add(new() { Parent = cable.Locator, Kind = "PropertyPlacement", Location = [60, 140] }); }
                Fixture.Base.Scene.Page.Placements.AddRange(_createdPlacements);
            }
            if (stage == "cores")
            { NativeObjects++; }
            if (Failure == stage || Failure == "rollback" && stage == "cores")
            { throw Failed(); }
            if (Failure == "cancel" && stage == "cores")
            { OperationContext.Current.Cancel(); }
        }
        public ConnectionCreatePlanner.Scene Read(string project, string page)
        {
            Assert.False(_abortedCreation);
            if (Events.Contains("undo", StringComparer.Ordinal))
            { RestorationRead = true; }
            if (_state > 0 && Fixture.Base.Request["items"]!.Count() > 1)
            { return new() { Graphical = _graphical }; }
            if (Failure == "restore" && RestorationRead)
            { Fixture.Base.Scene.Page.GridSize = 2; }
            return Fixture.Base.Scene;
        }
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => Fixture.Base.Read(projectPath, args);
        public Action Prepare(string project, string page, JArray symbols) => () => Stage("symbols");
        public (Action Create, Action Verify) PrepareCables(string project, string page, JObject request, JObject plan) => (() => Stage("definitions"), () =>
        {
            Assert.False(InTransaction);
            Assert.False(_abortedCreation);
            Assert.Equal(6, NativeObjects);
            Assert.Equal(_state, Commits);
            Events.Add("definitions-readback");
            if (Failure == "definition-readback")
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Cable readback differs: type_text: expected 'cable'; actual '<empty>'."); }
        }
        );
        public void BindCables(string project, string page, JObject request, JObject plan) { Assert.True(_generates > 0); Stage("bind"); }
        public void VerifyBindings(string project, string page, JObject request, JObject plan)
        {
            Assert.False(InTransaction);
            Assert.Equal(_state, Commits);
            Events.Add("bind-readback");
            if (Failure == "bind-readback")
            { throw Failed(); }
        }
        public void AssignCores(string project, string page, JObject request, JObject plan) { Assert.True(_generates >= 2); Stage("cores"); }
        public JArray ReadCores(string project, string page, JObject request, JObject plan)
        {
            Assert.False(InTransaction);
            Assert.Equal(_state, Commits);
            Assert.False(_abortedCreation);
            Events.Add("readback");
            var row = new JObject { ["designation"] = "+A1-W3", ["type"] = Fixture.Cable["type_text"]!.DeepClone(), ["label"] = "1", ["colour"] = "BK", ["oid"] = 9, ["pins"] = new JArray("17/3/0", "17/4/0") };
            string? mismatch = Failure?.StartsWith("final-", StringComparison.Ordinal) == true
                ? Events.Count(e => e == "readback") == 2 ? Failure.Substring(6) : null : Failure;
            if (mismatch is "label" or "colour" or "type" or "designation")
            { row[mismatch] = "wrong"; }
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
            var before = _history.Pop();
            Save(before.State);
            NativeObjects = before.Objects;
            if (NativeObjects == 0)
            { Fixture.Base.Scene.Page.Placements.RemoveAll(_createdPlacements.Contains); _createdPlacements.Clear(); }
            if (_state == 0)
            { RestoreContent(); }
        }
        private void RestoreContent()
        {
            ConnectionCreatePlanner.Scene scene = Fixture.Base.Scene;
            if (RenumberOnUndo)
            {
                string? Pin(string? key) => key is null ? null : string.Join("/", key.Split('/').Select((part, i) => i == 1 ? (int.Parse(part, System.Globalization.CultureInfo.InvariantCulture) + 100).ToString(System.Globalization.CultureInfo.InvariantCulture) : part));
                foreach (DrawingPlacement placement in scene.Page.Placements.Where(p => p.Parent is null))
                { placement.Locator.Oid += 100; placement.Locator.Digest = new string('f', 64); }
                scene.Pins = scene.Pins.Reverse().ToDictionary(p => Pin(p.Key)!, p => p.Value, StringComparer.Ordinal);
                foreach (ConnectionRecord connection in scene.Logical)
                { (connection.StartDevice, connection.EndDevice) = (Pin(connection.EndDevice), Pin(connection.StartDevice)); }
                foreach (var wire in Fixture.Inventory.Wires)
                { wire.Locator.Oid += 100; wire.Locator.Digest = new string('e', 64); wire.Pins = wire.Pins.Select(p => Pin(p)!).ToArray(); wire.Cable = "+"; }
            }
            switch (RestorationDifference)
            {
                case "page":
                    scene.Page.GridSize = 2;
                    break;
                case "placements":
                    scene.Page.Placements[0].Kind = "Different";
                    break;
                case "pins":
                    scene.Pins.First().Value.Direction = "Down";
                    break;
                case "graphical":
                    scene.Graphical.Clear();
                    break;
                case "endpoints":
                    scene.Logical[0].EndDevice = scene.Logical[0].StartDevice;
                    break;
                case "cableNames":
                    Fixture.Inventory.Names.Add("+A1-W3");
                    break;
                case "cores":
                    Fixture.Inventory.Wires[0].Colour = "wrong";
                    break;
            }
        }
        public ITransactionScopeFactory PlacementTransactions(string project) => this;
        public ITransactionScope Begin(string undoDescription) { Assert.False(InTransaction); InTransaction = true; return new Scope(this, _state, NativeObjects); }
        private sealed class Scope(Creator owner, int before, int objects) : ITransactionScope
        {
            public void Commit() { if (owner.Failure == "commit") { throw Failed(); } owner.Commits++; owner._history.Push((before, objects)); }
            public void Rollback()
            {
                owner.Aborts++;
                owner._abortedCreation = owner.NativeObjects != objects;
                if (owner.Failure == "rollback")
                { throw Failed(); }
                owner.Save(before);
            }
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
