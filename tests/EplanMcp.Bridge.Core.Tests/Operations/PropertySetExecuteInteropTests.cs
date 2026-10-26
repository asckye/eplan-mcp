using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class PropertySetExecuteInteropTests
{
    [Fact]
    public void Invoke_RegisteredB1Pair_RoundTripsPlanAndExecutesWithoutHostWhitelistPermission()
    {
        var sessions = new List<FakePropertySetSession>();
        IPropertySetSession Open(string project)
        {
            Assert.Equal(PropertySetPlannerTests.Project, project);
            var session = new FakePropertySetSession();
            sessions.Add(session);
            return session;
        }

        var transactions = new FakeTransactions();
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter(
            [new PropertySetPlanOperation(Open, 2026), new PropertySetExecuteOperation(Open, transactions, 2026)],
            dispatcher, null, new ListLog(), TimeSpan.FromSeconds(30));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        PropertySetArgs args = PropertySetExecuteTests.Args();

        Assert.Contains(OperationNames.PropertySetPlan, router.SupportedOperations);
        Assert.Contains(OperationNames.PropertySetExecute, router.SupportedOperations);
        JToken plan = router.Invoke(OperationNames.PropertySetPlan, JToken.Parse(JsonSerializer.Serialize(args, options)), "b1-plan");
        args.Approved = JsonSerializer.Deserialize<PropertySetPlan>(plan.ToString(), options)!;
        JToken execution = router.Invoke(OperationNames.PropertySetExecute, JToken.Parse(JsonSerializer.Serialize(args, options)), "b1-execute");
        PropertySetData data = JsonSerializer.Deserialize<PropertySetData>(execution.ToString(), options)!;

        Assert.Equal(2, dispatcher.Calls);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(ExecutionState.NotStarted, args.Approved.Execution);
        Assert.Equal(ExecutionState.Committed, data.Execution);
        Assert.Equal(2, data.Plan.Changes.Count);
        Assert.Empty(PropertyWriteWhitelist.Entries);
        Assert.All(sessions, session => session.AssertDisposed());
    }

    [Theory]
    [InlineData("commit", ExecutionState.Committed)]
    [InlineData("stale", ExecutionState.NotStarted)]
    [InlineData("mismatch", ExecutionState.RolledBack)]
    [InlineData("rollback_failure", ExecutionState.Unknown)]
    [InlineData("cancel", ExecutionState.RolledBack)]
    public void Invoke_SerializedWriteAndFailure_RecordsRecoverableOutcome(string scenario, string expected)
    {
        using var session = new FakePropertySetSession();
        var transactions = new FakeTransactions { Snapshot = session.RestorePoint };
        var dispatcher = new InlineDispatcher();
        var operation = new PropertySetExecuteOperation(_ => session, transactions, 2026);
        var router = new OperationRouter([operation], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(30));
        PropertySetArgs args = PropertySetExecuteTests.Args();
        if (scenario == "stale")
        {
            args.Approved.Guards[0].Before[0].Text += "stale";
        }

        if (scenario == "rollback_failure")
        {
            transactions.FailAt = "Rollback";
        }

        session.AfterWrite = _ =>
        {
            if (scenario is "mismatch" or "rollback_failure")
            {
                session.Objects[1].Properties[20011].Texts["de_DE"] = "lost";
            }

            if (scenario == "cancel")
            {
                router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "p7-write" }, "cancel");
            }
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JToken input = JToken.Parse(JsonSerializer.Serialize(args, options));
        if (scenario == "commit")
        {
            PropertySetData data = JsonSerializer.Deserialize<PropertySetData>(router.Invoke(operation.Name, input, "p7-write").ToString(), options)!;
            Assert.Equal(expected, data.Execution);
            Assert.Equal(2, data.Plan.Changes.Count);
        }
        else
        {
            BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => router.Invoke(operation.Name, input, "p7-write"));
            Assert.Equal(expected, error.Error.Execution);
        }

        Assert.Equal(1, dispatcher.Calls);
        JObject query = new() { ["requestId"] = "p7-write" };
        Assert.Equal(expected, (string?)router.Invoke(OperationNames.OutcomeGet, query, "outcome")["status"]);
        Assert.Throws<BridgeErrorException>(() => router.Invoke(operation.Name, input, "p7-write"));
        Assert.Equal(1, dispatcher.Calls);
        session.AssertDisposed();
    }
}
