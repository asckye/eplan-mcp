using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class PlacementMoveEvidenceTests
{
    [Fact]
    public async Task SavedPlan_ReReadUnrelatedPageChange_IsStale()
    {
        var f = new MoveFixture();
        JObject preview = f.Plan();
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(file, preview.ToString(), TestContext.Current.CancellationToken);
            JObject saved = JObject.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            f.Scene.Page.Placements.Add(MoveFixture.Placement(9, 'd', [80, 80]));
            BridgeError error = Assert.Throws<BridgeErrorException>(() => f.Plan((string)saved["fingerprint"]!)).Error;
            Assert.Equal(ErrorReason.PlanStale, error.Reason);
            Assert.Equal(ExecutionState.NotStarted, error.Execution);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task DetachedScene_ZeroDeltaOrSameAbsolute_IsNoOpAndUnchanged()
    {
        var f = new MoveFixture();
        f.Connect();
        f.Child('b', [22, 22]);
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            string before = JsonConvert.SerializeObject(f.Scene);
            await File.WriteAllTextAsync(file, before, TestContext.Current.CancellationToken);
            ConnectionCreatePlanner.Scene scene = JsonConvert.DeserializeObject<ConnectionCreatePlanner.Scene>(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!;
            foreach (JObject move in new[] { MoveFixture.Move(f.Root.Locator, [0, 0]), new JObject { ["locator"] = f.Request["moves"]![0]!["locator"]!.DeepClone(), ["x_mm"] = 20, ["y_mm"] = 20 } })
            {
                f.Request["moves"] = new JArray(move);
                JObject result = PlacementMovePlanner.Plan(f.Request.ToString(), scene);
                Assert.True((bool)result["noOp"]!);
                Assert.Equal(ExecutionState.NotStarted, (string?)result["execution"]);
                Assert.Empty(result["graphicalChanges"]!);
                Assert.Empty(result["connectionChanges"]!);
                Assert.Equal(before, JsonConvert.SerializeObject(scene));
                Assert.Equal(before, await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            }
            f.Root.Locator.Digest = new string('e', 64);
            f.Request["moves"] = new JArray(MoveFixture.Move(f.Root.Locator, [0, 0]));
            Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => PlacementMovePlanner.Plan(f.Request.ToString(), scene)).Error.Reason);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("child")]
    [InlineData("grid")]
    [InlineData("connection")]
    public void ApprovalEvidence_ChangesInvalidatePreview(string change)
    {
        var f = new MoveFixture();
        string fingerprint = (string)f.Plan()["fingerprint"]!;
        switch (change)
        {
            case "child":
                f.Child('c', [22, 22]);
                break;
            case "grid":
                f.Scene.Page.GridSize = 2.5;
                break;
            case "connection":
                f.Scene.Logical.Add(new() { StartDevice = "17/1/0", EndDevice = "17/9/0" });
                break;
        }
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => f.Plan(fingerprint)).Error.Reason);
    }
}
