using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class InterruptionNameReadbackTests
{
    [Theory]
    [InlineData("=GAC-L1", "-L1", true)]
    [InlineData("-L1", "-L1", false)]
    [InlineData("L1", "-L1", false)]
    [InlineData("=GAC-L1", "=GAC-L1", false)]
    [InlineData("+UC1-L1", "-L1", true)]
    [InlineData("+UC1-L1", "=+UC1-L1", false)]
    public void Unscoped_ExplicitStructureLoss_AppendsWarning(string requested, string stored, bool warning)
    {
        var fixture = new InterruptionPointFixture
        {
            Isolated = true,
            GeneratedEndpoints = [],
            StoredInterruptionName = stored,
            ExpectedPlannedName = requested[0] is '=' or '+' ? requested : "=F-L1"
        };
        JObject item = (JObject)fixture.Request["items"]!.Last!;
        item.Remove("counterpart_pages");
        item["name"] = requested;
        fixture.Request["items"] = new JArray(item);

        JObject result = fixture.Execute();

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(warning, ((string?)result["warning"])?.Contains("unique without structure", StringComparison.Ordinal) == true);
        Assert.Empty(fixture.Generates);
        if (warning)
        {
            Assert.Contains("Interruption point '" + requested + "' was stored as '" + stored + "'", (string?)result["warning"], StringComparison.Ordinal);
            Assert.Contains("pairs with every '" + stored + "' point", (string?)result["warning"], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scoped_StructureLoss_StillRollsBackWithTheWarningInItsError(bool changedEndpoints)
    {
        var fixture = new InterruptionPointFixture
        {
            StoredInterruptionName = "-L1",
            ExpectedPlannedName = "=GAC-L1"
        };
        fixture.Peers.Clear();
        fixture.Request["items"]!.Last!["name"] = "=GAC-L1";
        if (changedEndpoints)
        {
            fixture.GeneratedEndpoints = [["17/1/0", InterruptionPointPlanning.Endpoint(ConnectionCreateFixture.PageName, "-L1")]];
        }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => fixture.Execute()).Error;

        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Contains("Interruption point '=GAC-L1' was stored as '-L1'", error.Message, StringComparison.Ordinal);
        Assert.Contains("unique without structure", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.State);
        Assert.Empty(fixture.Generates);
    }

    [Theory]
    [InlineData("=GAC-L1", "-L1", true)]
    [InlineData("=GAC-L1", "-L2", false)]
    [InlineData("=GAC+UC1-L1", "=GAC-L1", true)]
    [InlineData("=GAC-L1", "=GAC1-L1", false)]
    [InlineData("=GAC-L1", "=GAC-L1", false)]
    public void OnlyStructureLossPreservesTheDevice(string requested, string stored, bool permitted)
    {
        var readback = new InterruptionNameReadback(requested, stored);

        Assert.Equal(permitted, readback.KeepsDeviceWithoutStructure);
    }
}
