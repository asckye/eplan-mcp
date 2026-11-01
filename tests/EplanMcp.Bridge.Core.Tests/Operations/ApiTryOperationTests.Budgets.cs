using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class ApiTryOperationTests
{
    private static ApiTryArgs GridSizeViaPageArgs(double value)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Page.GridSize");
        JObject parameters = JObject.Parse(args.Parameters);
        parameters["target"]!["type"] = 17;
        parameters["target"]!["oid"] = 42;
        parameters["via"] = "Page";
        parameters["value"] = value;
        args.Parameters = parameters.ToString(Formatting.None);
        return args;
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(4)]
    public void GridSizeViaPage_PlanningAndObservationsDoNotConsumeMemberBudget(double value)
    {
        ApiTryArgs args = GridSizeViaPageArgs(value);
        double seconds = 0;
        _dispatch.OnInspect = () => seconds += 19;
        _dispatch.OnRead = () => seconds += 15;
        _dispatch.OnInvoke = () => seconds++;
        string original = value == 2.5 ? "4" : "2.5";
        _dispatch.Value = original;
        args.Fingerprint = Plan(args).Fingerprint;
        var execute = new ApiTryExecuteOperation(Planner(), _dispatch, _transactions, () =>
        {
            double start = seconds;
            return () => TimeSpan.FromSeconds(seconds - start);
        });

        var data = (ApiTryData)execute.Execute(args)!;

        Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), data.Value);
        Assert.Equal(JsonConvert.SerializeObject(original), data.Before["property:" + args.Group]);
        Assert.Equal(JsonConvert.SerializeObject(data.Value), data.After["property:" + args.Group]);
        Assert.Equal(3, _dispatch.Inspections);
        Assert.Equal(1, _dispatch.Invocations);
        Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GridSizeViaPage_MemberReturnedOverBudget_IsKnownOnlyAfterSuccessfulRollback(bool rollbackFails)
    {
        ApiTryArgs args = GridSizeViaPageArgs(2.5);
        _dispatch.Value = "4";
        _transactions.Snapshot = () => { string original = _dispatch.Value; return () => _dispatch.Value = original; };
        args.Fingerprint = Plan(args).Fingerprint;
        if (rollbackFails)
        { _transactions.FailAt = "Rollback"; }
        double seconds = 0;
        _dispatch.OnInvoke = () => seconds = 31;
        var execute = new ApiTryExecuteOperation(Planner(), _dispatch, _transactions, () => () => TimeSpan.FromSeconds(seconds));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => execute.Execute(args)).Error;

        Assert.Equal(rollbackFails ? (ErrorReason.OutcomeUnknown, ExecutionState.Unknown) : (ErrorReason.Timeout, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(rollbackFails ? "2.5" : "4", _dispatch.Value);
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
        Assert.Equal(1, _dispatch.Invocations);
    }

    [Fact]
    public void GridSizeViaPage_RecordedFunctionResolvesBeforeUnrelatedCatalogEnumerations()
    {
        ApiTryArgs args = GridSizeViaPageArgs(2.5);
        var identity = new SnapshotObject { Kind = "function", Type = 17, Oid = 42, Page = "=GAA+A1&EFS1/1", Name = "=GAA+A1-FC1" };
        var locator = new DrawingLocator { Project = @"D:\scratch\Scratch", Page = identity.Page, Type = identity.Type, Oid = identity.Oid, Digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(identity)) };
        JObject parameters = JObject.Parse(args.Parameters);
        parameters["target"] = new JObject { ["project"] = locator.Project, ["page"] = locator.Page, ["type"] = locator.Type, ["oid"] = locator.Oid, ["digest"] = locator.Digest };
        args.Parameters = parameters.ToString(Formatting.None);
        _dispatch.TargetCheck = input =>
        {
            JObject p = JObject.Parse(input.Parameters);
            string[] shape = ApiTryCatalog.Shape(input.Group, p.Properties().Select(v => v.Name))!;
            string[] kinds = ApiTryCatalog.ViaKinds(shape[0], (string)p["via"]!);
            SnapshotObject found = ApiTryTargets.Resolve(p["target"]!.ToObject<DrawingLocator>()!, kinds, [identity],
                _ => throw new InvalidOperationException("Must not rescan the project for unrelated via kinds."),
                (obj, kind) => obj.Kind == kind ? (obj, obj.Page!) : null);
            Assert.Same(identity, found);
        };
        args.Fingerprint = Plan(args).Fingerprint;

        Assert.Equal("2.5", ((ApiTryData)Executor().Execute(args)!).Value);
        Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }
}
