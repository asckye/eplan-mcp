using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class ApiTryOperationTests
{
    [Theory]
    [InlineData(2, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void RotateLineArray_PreflightSnapshotDoesNotConsumeExecutionBudget(int invocationSeconds, bool commits)
    {
        // q-op-arrays-ApiTryTests.log: rotate one discovered Line by 90 degrees around (0, 0).
        ApiTryArgs args = Args("O:Eplan.EplApi.HEServices.Edit.Rotate");
        JObject p = JObject.Parse(args.Parameters);
        JObject line = (JObject)p["target"]!.DeepClone();
        line["oid"] = 2;
        p["target"]!["page"] = string.Empty;
        p["plcmnts"] = new JArray(line);
        p["oPntStart"] = new JObject { ["x"] = 0, ["y"] = 0 };
        p["angle"] = 90;
        args.Parameters = p.ToString();
        args.Fingerprint = Plan(args).Fingerprint;
        TimeSpan now = TimeSpan.Zero;
        _dispatch.OnInspect = () => now += TimeSpan.FromSeconds(18);
        _dispatch.OnInvoke = () => now += TimeSpan.FromSeconds(invocationSeconds);
        _dispatch.ChangeOnInvoke = true;
        var executor = new ApiTryExecuteOperation(Planner(), _dispatch, _transactions, () =>
        {
            TimeSpan started = now;
            return () => now - started;
        });

        if (commits)
        {
            var data = (ApiTryData)executor.Execute(args)!;
            Assert.Equal("before", data.Before["snapshot:1"]);
            Assert.Equal("after", data.After["snapshot:1"]);
            Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        }
        else
        {
            BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => executor.Execute(args));
            Assert.Equal((ErrorReason.Timeout, ExecutionState.RolledBack), (failure.Error.Reason, failure.Error.Execution));
            Assert.Equal(["Begin AI: eplan_api_try", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
        }
        Assert.Equal(TimeSpan.FromSeconds((commits ? 36 : 18) + invocationSeconds), now);
        Assert.Equal(commits ? 3 : 2, _dispatch.Inspections); // initial plan, execution preflight, post-call observation
        Assert.Equal(1, _dispatch.Invocations);
    }

    [Theory]
    [InlineData("System.Boolean", "[true,false]", true)]
    [InlineData("System.Int32", "[2147483648]", false)]
    [InlineData("System.Int64", "[9223372036854775807]", true)]
    [InlineData("System.Int64", "[9223372036854775808]", false)]
    [InlineData("System.Double", "[1,2.5]", true)]
    [InlineData("enum", "[\"One\"]", true)]
    [InlineData("enum", "[\"one\"]", false)]
    [InlineData("Eplan.EplApi.Base.PointD", "[{\"x\":1,\"y\":2}]", true)]
    [InlineData("Eplan.EplApi.Base.PointD", "[{\"x\":1,\"y\":\"2\"}]", false)]
    public void BridgeChecksEverySupportedValueElement(string type, string json, bool valid) => Assert.Equal(valid,
        ApiTryPlanOperation.ValidValue(type == "enum" ? ["array", "enum", "One", "Two"] : ["array", type], JToken.Parse(json)));
    [Theory]
    [InlineData(0, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void ArrayElementLimitIsCheckedIndependentlyByBridge(int count, bool valid)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Group.InsertSubPlacements");
        JObject parameters = JObject.Parse(args.Parameters);
        parameters["placements"] = new JArray(Enumerable.Range(0, count).Select(_ => parameters["target"]!.DeepClone()));
        args.Parameters = parameters.ToString();
        if (valid)
        { Assert.Equal("write", Assert.Single(Plan(args).Changes).Action); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason); }
        Assert.Equal(0, _dispatch.Invocations);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void ArrayElementsCannotCoerceOrNest(string invalid)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Group.InsertSubPlacements");
        JObject p = JObject.Parse(args.Parameters);
        p["placements"] = new JArray(JToken.Parse(invalid));
        args.Parameters = p.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Equal(0, _dispatch.Inspections);
    }
    [Fact]
    public void EveryArrayLocatorMustBelongToThePlannedProject()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Group.InsertSubPlacements");
        JObject p = JObject.Parse(args.Parameters);
        JObject foreign = (JObject)p["target"]!.DeepClone();
        foreign["project"] = @"D:\real\Project";
        p["placements"] = new JArray(p["target"]!.DeepClone(), foreign);
        args.Parameters = p.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Equal(0, _dispatch.Inspections);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionResultsAreDetachedInsideTheSingleWriteTransaction(bool fail)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Group.InsertSubPlacements");
        JObject p = JObject.Parse(args.Parameters);
        p["placements"] = new JArray(p["target"]!.DeepClone());
        args.Parameters = p.ToString();
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.MethodResult = new JObject { ["returnValue"] = true, ["outputs"] = new JObject { ["points"] = new JArray(new JObject { ["x"] = 1, ["y"] = 2 }) } };
        if (fail)
        {
            _dispatch.Failure = new InvalidOperationException("Cannot detach result.");
            Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error.Execution);
            Assert.Contains("Rollback", _transactions.Calls);
            Assert.DoesNotContain("Commit", _transactions.Calls);
        }
        else
        {
            ApiTryData result = (ApiTryData)Executor().Execute(args)!;
            Assert.True(JToken.DeepEquals((JToken)_dispatch.MethodResult, JToken.Parse(result.ResultJson!)));
            Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        }
        Assert.Equal(1, _dispatch.Invocations);
    }
}
