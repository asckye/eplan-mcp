using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class InterruptionPointTests
{
    [Fact]
    public void ChangedPluginSource_WithSameSymbol_InvalidatesFingerprint()
    {
        var fake = new InterruptionPointFixture();
        fake.Request["plugin_sources"] = new JArray("standard@1.0.0");
        string fingerprint = (string)fake.Plan()["fingerprint"]!;
        fake.Request["plugin_sources"] = new JArray("standard@2.0.0");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Plan(fingerprint)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(fake.Placement.Calls);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(3L)]
    [InlineData(long.MaxValue)]
    public void SortCode_ReachesPlacementAndReadsBack(long sortCode)
    {
        var fake = new InterruptionPointFixture();
        fake.Request["items"]!.Last!["sort_code"] = sortCode;
        JObject result = fake.Execute();
        Assert.Equal(sortCode, fake.PlacedSortCode);
        Assert.Equal(sortCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (string?)Assert.Single(result["interruptionPoints"]!, p => (long?)p["oid"] == 91)["sortCode"]);
        Assert.Single(fake.Placement.Calls, c => c == "Commit");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"3\"")]
    [InlineData("null")]
    [InlineData("9223372036854775808")]
    public void SortCode_InvalidValue_IsRejectedBeforePlacement(string value)
    {
        var fake = new InterruptionPointFixture();
        fake.Request["items"]!.Last!["sort_code"] = JToken.Parse(value);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Plan()).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/items/1/sort_code", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(fake.Placement.Calls);
    }

    [Fact]
    public void SortCode_Omitted_IsNotWritten()
    {
        var fake = new InterruptionPointFixture();
        Assert.Null(Assert.Single(fake.Plan()["symbols"]!, s => s["interruption"] is not null)["interruption"]!["sort_code"]);
        fake.Execute();
        Assert.Null(fake.PlacedSortCode);
    }

    [Fact]
    public void Symbol_NamesAnotherSpecialInterruptionSymbol_DefaultIsBp()
    {
        // A83: EPLAN's insert command uses BP; any SPECIAL interruption symbol remains selectable.
        var fake = new InterruptionPointFixture();
        fake.Base.Symbols.Add(ConnectionCreateFixture.Variant("BPX", "InterruptionPoint", 3, "Up"));
        fake.Base.Scene.SymbolBounds.Add("BPX/3", [-1, -1, 1, 1]);
        string Planned() => (string)Assert.Single(fake.Plan()["symbols"]!, s => s["interruption"] is not null)["symbol"]!["name"]!;
        Assert.Equal("BP", Planned());
        fake.Request["items"]!.Last!["symbol"] = "BPX";
        Assert.Equal("BPX", Planned());
        fake.Request["items"]!.Last!["symbol"] = "NONE";
        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeErrorException>(() => fake.Plan()).Error.Reason);
    }

    [Fact]
    public void SortCode_ReadbackMismatch_RollsBackPlacement()
    {
        var fake = new InterruptionPointFixture { Failure = "sort_code" };
        fake.Request["items"]!.Last!["sort_code"] = 3;
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error.Execution);
        Assert.Equal(0, fake.State);
    }

    [Fact]
    public void Plan_ExplicitScopeAndPairEvidence_DoesNotWrite()
    {
        var fake = new InterruptionPointFixture();
        JObject plan = fake.Plan();
        Assert.Equal([ConnectionCreateFixture.PageName, InterruptionPointFixture.PeerPage], plan["affectedPages"]!.Values<string>());
        Assert.Contains(plan["symbols"]!, s => (string?)s["symbol"]!["name"] == "BP");
        Assert.Equal(InterruptionPointFixture.BusinessName, (string?)plan["interruptionScope"]!["points"]![0]!["name"]);
        Assert.Empty(fake.Placement.Calls);
        Assert.Empty(fake.Generation.Calls);
    }

    [Theory]
    [InlineData("name", " ", "/name")]
    [InlineData("direction", "Diagonal", "/items")]
    [InlineData("counterpart_pages", "page", "/counterpart_pages")]
    [InlineData("crossReference", "manual", "/crossReference")]
    public void InvalidInterruption_IsRejectedWithPointer(string member, string value, string path)
    {
        var fake = new InterruptionPointFixture();
        fake.Request["items"]!.Last![member] = value;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Plan()).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Contains(path, error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(fake.ScopeReads);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("position")]
    [InlineData("crossReference")]
    [InlineData("drawing")]
    public void CounterpartChangedAfterApproval_StaleBeforeEitherTransaction(string field)
    {
        var fake = new InterruptionPointFixture();
        JObject args = fake.Arguments();
        if (field == "drawing")
        { fake.DrawingRevision = "changed"; }
        else
        { fake.Peers[0]![field] = field == "position" ? new JArray(84d, 80d) : "changed"; }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(fake.Placement.Calls);
        Assert.Empty(fake.Generation.Calls);
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("paired")]
    [InlineData("outside")]
    [InlineData("duplicate")]
    public void AmbiguousOrIncompleteScope_RefusedBeforeWriting(string condition)
    {
        var fake = new InterruptionPointFixture();
        switch (condition)
        {
            case "ambiguous":
                fake.Peers.Add(InterruptionPointFixture.Point(InterruptionPointFixture.PeerPage, 92, [88, 88], "Up"));
                break;
            case "paired":
                fake.Peers[0]!["counterparts"] = new JArray(new JObject { ["page"] = InterruptionPointFixture.PeerPage, ["oid"] = 92 });
                break;
            case "outside":
                fake.Peers[0]!["name"] = "other";
                fake.Peers[0]!["counterparts"] = new JArray(new JObject { ["page"] = "outside", ["oid"] = 92 });
                break;
            case "duplicate":
                ((JArray)fake.Request["items"]!.Last!["counterpart_pages"]!).Add(InterruptionPointFixture.PeerPage);
                break;
        }
        Assert.Throws<BridgeErrorException>(() => fake.Plan());
        Assert.Empty(fake.Placement.Calls);
    }

    [Fact]
    public void Execute_NativeReferencesBothWays_SingleUndoScope()
    {
        var fake = new InterruptionPointFixture();
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(2, result["interruptionPoints"]!.Count());
        Assert.All(result["interruptionPoints"]!, p => Assert.StartsWith("native:", (string?)p["crossReference"], StringComparison.Ordinal));
        Assert.Equal([ConnectionCreateFixture.PageName, InterruptionPointFixture.PeerPage], Assert.Single(fake.Generates));
        Assert.Single(fake.Placement.Calls, c => c == "Commit");
        Assert.Empty(fake.Generation.Calls);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Equal(0, fake.Undos);
    }

    [Fact]
    public void SamePagePair_SecondSameNamePoint_PairsOnTheTargetPage()
    {
        // Customer page 13: I2 and I5 share a name on one page and reference each other (A65).
        var fake = new InterruptionPointFixture();
        fake.Peers[0]!["page"] = ConnectionCreateFixture.PageName;
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(2, result["interruptionPoints"]!.Count());
        Assert.All(result["interruptionPoints"]!, p => Assert.Equal(ConnectionCreateFixture.PageName, (string?)Assert.Single(p["counterparts"]!)["page"]));
    }

    [Fact]
    public void MissingCounterpart_RemainsExplicitlyUnpaired()
    {
        var fake = new InterruptionPointFixture();
        fake.Peers.Clear();
        JToken point = Assert.Single(fake.Execute()["interruptionPoints"]!);
        Assert.Empty(point["counterparts"]!);
        Assert.Null((string?)point["crossReference"]);
    }

    [Theory]
    [InlineData("generate", 0)]
    [InlineData("pair", 0)] // live T7b: pairing is read only after the generation commit
    [InlineData("postcommit", 0)]
    public void GenerationOrReferenceFailure_CompensatesAndVerifiesScope(string failure, int undos)
    {
        var fake = new InterruptionPointFixture { Failure = failure };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal(undos, fake.Undos);
        Assert.Equal(0, fake.State);
    }

    [Fact]
    public void PlacementAbort_DoesNotReadDeletedObjectsOrGenerate()
    {
        var fake = new InterruptionPointFixture { Failure = "placement" };
        Assert.Throws<BridgeErrorException>(() => fake.Execute());
        Assert.Equal(0, fake.State);
        Assert.Equal(3, fake.ScopeReads.Count); // Planning, preflight and fresh restoration evidence.
        Assert.DoesNotContain("CloseUndo", fake.Placement.Calls);
        Assert.Empty(fake.Generates);
    }

    [Fact]
    public void EmptyNativeReferenceText_StillCommits_WhenPairingIsCorrect()
    {
        // Live T7b: property 24300 stays empty after the commit; the native pairing is the proof.
        var fake = new InterruptionPointFixture { Failure = "empty_reference" };
        fake.Execute();
        Assert.Equal(0, fake.Undos);
    }

    [Fact]
    public void CompensationFailure_ReportsUnknown()
    {
        var fake = new InterruptionPointFixture { Failure = "undo" };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Fact]
    public void ConnectedPair_JoinsNativeGraphsAcrossExplicitPages()
    {
        var fake = new InterruptionPointFixture();
        fake.ConnectPair();
        JObject plan = fake.Plan();
        JToken group = Assert.Single(plan["logicalAfterGenerate"]!, g => g["pins"]!.Values<string>().Contains("17/3/0", StringComparer.Ordinal));
        Assert.Equal(["17/3/0", "17/9/0"], group["pins"]!.Values<string>());
        Assert.False((bool)group["potentialStartIsNull"]!);
        Assert.Equal(ExecutionState.Committed, (string?)fake.Execute()["execution"]);
    }

    [Fact]
    public void ConnectedPair_ThroughAnOffsetNode_StillJoinsTheNet()
    {
        // Live 2026-11-02 (customer page): the scope held function pins only, so TOUR variant 0's offset pin cut the net.
        var fake = new InterruptionPointFixture { OffsetNode = true };
        fake.ConnectPair();
        JToken group = Assert.Single(fake.Plan()["logicalAfterGenerate"]!, g => g["pins"]!.Values<string>().Contains("17/3/0", StringComparer.Ordinal));
        Assert.Equal(["17/3/0", "17/9/0"], group["pins"]!.Values<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConnectedPair_NativeInterruptionEnds_AllowOnlyThePredictedNet(bool crossNet)
    {
        var fake = new InterruptionPointFixture();
        fake.ConnectPair();
        fake.GeneratedEndpoints = [["17/1/0", "17/2/0"], ["17/3/0", "17/9/0"],
            [crossNet ? "17/1/0" : "17/3/0", InterruptionPointPlanning.Endpoint(ConnectionCreateFixture.PageName, InterruptionPointFixture.BusinessName)],
            [InterruptionPointPlanning.Endpoint(InterruptionPointFixture.PeerPage, InterruptionPointFixture.BusinessName), "17/9/0"]];
        if (crossNet)
        { Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error.Execution); }
        else
        { Assert.Equal(ExecutionState.Committed, (string?)fake.Execute()["execution"]); }
    }

    [Fact]
    public void BpfOffset_UsesElectricalPositionAndSmallestMirroredVariant()
    {
        var fake = new InterruptionPointFixture();
        fake.Request["items"]!.Last!["symbol"] = "BPF";
        fake.Base.Symbols.Single(s => s.Name == "BPF").ConnectionPoints[0].Location = [0, 2];
        fake.Base.Symbols.Add(ConnectionCreateFixture.Variant("BPF", "InterruptionPoint", 4, "Up"));
        JToken row = Assert.Single(fake.Plan()["symbols"]!, s => s["interruption"] is not null);
        Assert.Equal([60d, 58d], row["position"]!.Values<double>());
        Assert.Equal(0, (int)row["symbol"]!["variant"]!);
    }

    [Fact]
    public void IsolatedInterruption_NoRealGeneration_CommitsWithOneUndo()
    {
        var fake = new InterruptionPointFixture();
        fake.Request["items"] = new JArray(fake.Request["items"]!.Last!.DeepClone());
        fake.GeneratedEndpoints = [];
        fake.Base.Scene.Graphical = [];
        fake.Isolated = true;
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Empty(result["actualEndpoints"]!);
        Assert.Single(fake.Placement.Calls, c => c == "CloseUndo");
        Assert.Empty(fake.Generation.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DanglingInterruption_LineWithoutCounterpartDevice_CommitsWithoutLogicalConnection(bool paired)
    {
        var fake = new InterruptionPointFixture { Dangling = true, GeneratedEndpoints = [] };
        fake.ConnectPair();
        if (!paired)
        { fake.Peers.Clear(); }
        fake.Request["items"] = new JArray(fake.Request["items"]!.Last!.DeepClone());
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(1, (int)result["undoSteps"]!);
        Assert.Empty(result["actualEndpoints"]!);
        Assert.Single(result["actualGraphicalAutoconnect"]!);
        Assert.Equal(paired ? 2 : 1, result["interruptionPoints"]!.Count());
    }

    [Theory]
    [InlineData("line")]
    [InlineData("generated_line")]
    [InlineData("peer_line")]
    [InlineData("peer_pin")]
    [InlineData("position")]
    [InlineData("pin")]
    [InlineData("name")]
    [InlineData("direction")]
    [InlineData("cancel")]
    public void GraphicalOrIdentityMismatchOrCancellation_RestoresWithoutClosingUndo(string failure)
    {
        var fake = new InterruptionPointFixture { Failure = failure };
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error.Execution);
        Assert.Equal(0, fake.State);
        Assert.DoesNotContain("CloseUndo", fake.Placement.Calls);
    }

    [Fact]
    public void ConnectedPair_MissingRealDeviceConnection_RollsBack()
    {
        var fake = new InterruptionPointFixture();
        fake.ConnectPair();
        fake.GeneratedEndpoints = [["17/1/0", "17/2/0"]];
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error.Execution);
        Assert.Equal(0, fake.State);
    }
}
