using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class PlacementMoveExecutionMediumTests
{
    [Theory]
    [InlineData("batch")]
    [InlineData("stale")]
    [InlineData("noop")]
    public async Task DetachedApprovalEvidence_MidBatchFailureStaleAndNoOp(string scenario)
    {
        var mover = new PlacementMoveExecutionTests.Mover();
        if (scenario == "noop")
        { mover.Fixture.Request["moves"]![0]!["delta_mm"] = new JArray(0, 0); }
        string before = BridgeJson.FromObject(mover.Scene).ToString();
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(file, PlacementMovePlanner.Plan(mover.Fixture.Request.ToString(), mover.Scene).ToString(), TestContext.Current.CancellationToken);
            string fingerprint = (string)JObject.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))["fingerprint"]!;
            if (scenario == "stale")
            { mover.Scene.Page.GridSize = 2; }
            if (scenario == "batch")
            { mover.Failure = "batch"; }
            if (scenario == "noop")
            {
                JObject result = mover.Execute(fingerprint);
                Assert.Equal(ExecutionState.NotStarted, (string?)result["execution"]);
            }
            else
            {
                BridgeError error = Assert.Throws<BridgeErrorException>(() => mover.Execute(fingerprint)).Error;
                Assert.Equal(scenario == "stale" ? ErrorReason.PlanStale : ErrorReason.EplanOperationFailed, error.Reason);
                Assert.Equal(scenario == "stale" ? ExecutionState.NotStarted : ExecutionState.RolledBack, error.Execution);
            }
            Assert.DoesNotContain("CloseUndo", mover.Transactions.Calls);
            if (scenario != "batch")
            { Assert.Empty(mover.Transactions.Calls); }
            if (scenario != "stale")
            { Assert.Equal(before, BridgeJson.FromObject(mover.Scene).ToString()); }
        }
        finally { File.Delete(file); }
    }
}
