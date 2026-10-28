using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class InterruptionPointTests
{
    [Fact]
    public void Plan_ExplicitScopeAndPairEvidence_DoesNotWrite()
    {
        var fake = new InterruptionPointFixture();
        JObject plan = fake.Plan();
        Assert.Equal([ConnectionCreateFixture.PageName, InterruptionPointFixture.PeerPage], plan["affectedPages"]!.Values<string>());
        Assert.Contains(plan["symbols"]!, s => (string?)s["symbol"]!["name"] == "BPF");
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
    public void Execute_NativeReferencesBothWays_SeparatePlacementAndGenerationScopes()
    {
        var fake = new InterruptionPointFixture();
        JObject result = fake.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(2, result["interruptionPoints"]!.Count());
        Assert.All(result["interruptionPoints"]!, p => Assert.StartsWith("native:", (string?)p["crossReference"], StringComparison.Ordinal));
        Assert.Equal([ConnectionCreateFixture.PageName, InterruptionPointFixture.PeerPage], Assert.Single(fake.Generates));
        Assert.Single(fake.Placement.Calls, c => c == "Commit");
        Assert.Single(fake.Generation.Calls, c => c == "Commit");
        Assert.Equal(0, fake.Undos);
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
    [InlineData("generate", 1)]
    [InlineData("cancel", 1)]
    [InlineData("pair", 1)]
    [InlineData("empty_reference", 1)]
    [InlineData("postcommit", 2)]
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
        Assert.Equal(2, fake.ScopeReads.Count); // planning and preflight only
        Assert.DoesNotContain("CloseUndo", fake.Placement.Calls);
        Assert.Empty(fake.Generates);
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
    public void BpfOffset_UsesElectricalPositionAndSmallestMirroredVariant()
    {
        var fake = new InterruptionPointFixture();
        fake.Base.Symbols.Single(s => s.Name == "BPF").ConnectionPoints[0].Location = [0, 2];
        fake.Base.Symbols.Add(ConnectionCreateFixture.Variant("BPF", "InterruptionPoint", 4, "Up"));
        JToken row = Assert.Single(fake.Plan()["symbols"]!, s => s["interruption"] is not null);
        Assert.Equal([60d, 58d], row["position"]!.Values<double>());
        Assert.Equal(0, (int)row["symbol"]!["variant"]!);
    }

    [Fact]
    public void IsolatedInterruption_NoRealGeneration_RefusedBeforeTransaction()
    {
        var fake = new InterruptionPointFixture();
        fake.Request["items"] = new JArray(fake.Request["items"]!.Last!.DeepClone());
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal((ErrorReason.CapabilityMissing, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(fake.Placement.Calls);
        Assert.Empty(fake.Generation.Calls);
    }
}
