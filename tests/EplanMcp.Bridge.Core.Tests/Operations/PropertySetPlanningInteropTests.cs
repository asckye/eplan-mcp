using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class PropertySetPlanningInteropTests
{
    [Fact]
    public void Plan_HostJsonThroughTestRouter_RoundTripsCompleteRawGuardsAndNoOp()
    {
        FakePropertyReadAdapter adapter = PropertySetPlannerTests.Adapter();
        adapter.Properties[20011].Texts["de_DE"] = new string('x', 650) + "\u202e";
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter([new TestPlanOperation(adapter)], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(30));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var args = new TestPlanArgs
        {
            Changes = [PropertySetPlannerTests.Change()],
        };

        JToken response = router.Invoke("test.property.plan", JToken.Parse(JsonSerializer.Serialize(args, options)), "planning");
        PropertySetPlan plan = JsonSerializer.Deserialize<PropertySetPlan>(response.ToString(), options)!;

        Assert.Equal(1, dispatcher.Calls);
        Assert.Equal(ExecutionState.NotStarted, plan.Execution);
        Assert.Single(plan.Plan.Changes);
        PropertySetGuard guard = Assert.Single(plan.Guards);
        Assert.Equal(651, PropertyValueComparison.TextIn(guard.Before, "de_DE").Length);
        Assert.Equal(PropertyValueComparison.TextIn(guard.Before, "de_DE"), PropertyValueComparison.TextIn(guard.After, "de_DE"));
        Assert.Equal(response, BridgeJson.FromObject(plan));
        Assert.Equal(PropertySetPlannerTests.Project, guard.Target.Project);
        Assert.Equal("Coil", guard.Target.Category);
        Assert.False(guard.Definition.IsReadOnly);
        adapter.AssertDisposed();

        args.Changes[0] = PropertySetPlannerTests.Change("旧文本");
        JToken noOp = router.Invoke("test.property.plan", JToken.Parse(JsonSerializer.Serialize(args, options)), "no-op");
        Assert.Empty(JsonSerializer.Deserialize<PropertySetPlan>(noOp.ToString(), options)!.Plan.Changes);
    }

    [Fact]
    public void Plan_StaleAndLanguageFailures_KeepPointersAndNotStartedAcrossSerializers()
    {
        FakePropertyReadAdapter adapter = PropertySetPlannerTests.Adapter();
        var router = new OperationRouter([new TestPlanOperation(adapter)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(30));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        PropertySetChange stale = PropertySetPlannerTests.Change();
        ObjectLocator locator = PropertySetPlannerTests.Locator(PropertySetPlannerTests.Function());
        locator.Name += "-changed";
        stale.Target = new PropertyTarget(locator: locator);
        var args = new TestPlanArgs
        {
            Changes = [stale, PropertySetPlannerTests.Change(language: "fr_FR")],
        };

        BridgeErrorException exception = Assert.Throws<BridgeErrorException>(() => router.Invoke("test.property.plan",
            JToken.Parse(JsonSerializer.Serialize(args, options)), "invalid-plan"));
        BridgeError error = JsonSerializer.Deserialize<BridgeError>(BridgeJson.FromObject(exception.Error).ToString(), options)!;

        Assert.Equal(ErrorReason.PlanStale, error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Execution);
        JArray violations = JArray.Parse(error.Metadata!["violations"]);
        Assert.Contains(violations, v => (string?)v["path"] == "/changes/0/target");
        Assert.Contains(violations, v => (string?)v["path"] == "/changes/1/language");
        Assert.Equal(0, adapter.Opens);
    }

    // P6's captured-reader seam; the registered B1 pair is covered by PropertySetExecuteInteropTests.
    private sealed class TestPlanOperation(FakePropertyReadAdapter adapter) : Operation<TestPlanArgs, PropertySetPlan>
    {
        public override string Name => "test.property.plan";

        protected override PropertySetPlan Execute(TestPlanArgs args) => new PropertySetPlanner(adapter).Plan(
            PropertySetPlannerTests.Project, 2026, args.Changes, [PropertySetPlannerTests.Function()],
            ["zh_CN", "de_DE"], CancellationToken.None);
    }

    public sealed class TestPlanArgs
    {
        public List<PropertySetChange> Changes { get; set; } = [];
    }
}
