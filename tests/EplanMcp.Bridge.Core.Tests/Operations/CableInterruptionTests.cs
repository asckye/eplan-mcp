using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class CableInterruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedPlan_PreservesCableImpactAndExplicitInterruptionScope_WithoutWriting(bool connected)
    {
        var (fake, _) = Setup();
        if (connected)
        { fake.ConnectPair(); }
        JObject plan = fake.Plan();
        Assert.Equal([ConnectionCreateFixture.PageName, InterruptionPointFixture.PeerPage], plan["affectedPages"]!.Values<string>());
        Assert.Equal(InterruptionPointFixture.BusinessName, (string?)plan["interruptionScope"]!["points"]![0]!["name"]);
        Assert.Contains(plan["symbols"]!, s => s["interruption"] is not null);
        Assert.Equal("-W3", (string?)Assert.Single(plan["coreImpact"]!)["designation"]);
        Assert.Equal(0, (int)plan["coreImpact"]![0]!["connection"]!["item_index"]!);
        Assert.Equal(JTokenType.Null, plan["undoSteps"]!.Type);
        Assert.Equal(ErrorReason.CapabilityMissing, (string?)plan["executionCapability"]);
        Assert.Empty(fake.Placement.Calls);
        Assert.Empty(fake.Generation.Calls);
    }

    [Fact]
    public void MixedExecute_IsCableCapabilityMissing_BeforeEitherTransaction()
    {
        var (fake, _) = Setup();
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute()).Error;
        Assert.Equal((ErrorReason.CapabilityMissing, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal("connection.create.cable.execute", error.Metadata!["missing"]);
        Assert.Equal(0, fake.State);
        Assert.Empty(fake.Placement.Calls);
        Assert.Empty(fake.Generation.Calls);
        Assert.Empty(fake.Generates);
    }

    [Theory]
    [InlineData("counterpart")]
    [InlineData("drawing")]
    [InlineData("cableInventory")]
    public void MixedGuard_ChangesToEitherFeature_AreStaleBeforeWriting(string change)
    {
        var (fake, inventory) = Setup();
        JObject args = fake.Arguments();
        switch (change)
        {
            case "counterpart":
                fake.Peers[0]!["position"] = new JArray(84d, 80d);
                break;
            case "drawing":
                fake.DrawingRevision = "changed";
                break;
            case "cableInventory":
                inventory.Names.Add("-W100");
                break;
        }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => fake.Execute(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(fake.Placement.Calls);
        Assert.Empty(fake.Generation.Calls);
    }

    private static (InterruptionPointFixture Fake, CableDefinitionPlanner.Inventory Inventory) Setup()
    {
        var fake = new InterruptionPointFixture();
        var inventory = new CableDefinitionPlanner.Inventory { Complete = true };
        fake.Base.Scene.ReadCables = () => inventory;
        ((JArray)fake.Request["items"]!).Add(CableDefinitionFixture.Definition("-W3", [[16, 4], [16, 12]],
            CableDefinitionFixture.Core(new JObject { ["item_index"] = 0 }, "1", "BK")));
        return (fake, inventory);
    }
}
