using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTryOperationTests
{
    private const string Group = "O:Eplan.EplApi.DataModel.Page.Name";
    private readonly Dispatcher _dispatch = new();
    private ApiTryPlanOperation Planner(int major = 2026) => new(major, new Catalog(), _dispatch);
    private static ApiTryArgs Args(string group = Group) => new()
    {
        Group = group, Project = "Scratch", Parameters = new JObject
        {
            ["target"] = new JObject { ["project"] = @"D:\scratch\Scratch", ["page"] = "=A/1", ["type"] = 1, ["oid"] = 1, ["digest"] = new string('a', 64) },
        }.ToString(Newtonsoft.Json.Formatting.None),
    };
    private WritePlan Plan(ApiTryArgs args) => (WritePlan)Planner().Execute((object)args)!;

    [Fact]
    public void Plan_RecordsRawParametersAndSignature_AndDoesNotInvoke()
    {
        ApiTryArgs args = Args();
        WritePlan plan = Plan(args);
        Assert.Equal(args.Parameters, plan.Details!["rawParameters"]);
        Assert.Equal("true", plan.Details["unverified"]);
        Assert.Contains("Page.Name", Assert.Single(plan.Changes).Property, StringComparison.Ordinal);
        Assert.Equal(0, _dispatch.Invocations);
    }

    [Theory]
    [InlineData("System.IO.File.Delete")]
    [InlineData("P:Eplan.EplApi.DataModel.Page.Name")]
    [InlineData("O:Eplan.EplApi.DataModel.Page.name")]
    public void NonGroupIdsAndUnknownNames_AreDenied(string group)
    {
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Plan(Args(group))).Error.Reason);
        Assert.Equal(0, _dispatch.Inspections);
    }

    [Fact]
    public void OtherMajorVersion_IsDeniedBeforeInspecting()
    {
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Planner(2025).Execute(Args())).Error.Reason);
        Assert.Equal(0, _dispatch.Inspections);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("type-string")]
    [InlineData("digest")]
    [InlineData("wrong-project")]
    [InlineData("extra-locator-field")]
    [InlineData("setter")]
    [InlineData("duplicate")]
    [InlineData("array")]
    public void BadShapes_AreRejectedWithoutInvoking(string mode)
    {
        ApiTryArgs args = Args();
        JObject p = JObject.Parse(args.Parameters);
        var locator = (JObject)p["target"]!;
        switch (mode)
        {
            case "missing": p.Remove("target"); break;
            case "type-string": locator["type"] = "1"; break;
            case "digest": locator["digest"] = new string('A', 64); break;
            case "wrong-project": locator["project"] = @"D:\real\Project"; break;
            case "extra-locator-field": locator["typeName"] = "System.IO.File"; break;
            case "setter": p["value"] = "new name"; break;
            case "array": p["target"] = new JArray(locator); break;
        }
        args.Parameters = mode == "duplicate" ? "{\"target\":{},\"target\":{}}" : p.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Equal(0, _dispatch.Invocations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MethodShape_RequiresActualBoolean(bool valid)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Function.IsDeviceLocked");
        JObject p = JObject.Parse(args.Parameters);
        p["bWithCDPs"] = valid ? new JValue(true) : new JValue("true");
        args.Parameters = p.ToString();
        if (valid) { Assert.Single(Plan(args).Changes); }
        else { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason); }
    }

    [Fact]
    public void RegisteredButUndispatchable_ProducesAnEmptyPlan()
    {
        WritePlan plan = Plan(Args("O:Eplan.EplApi.DataModel.Function.Create"));
        Assert.Equal("not_dispatchable_yet", plan.Details!["status"]);
        Assert.Empty(plan.Changes);
        Assert.Equal(0, _dispatch.Inspections);
    }

    [Fact]
    public void Execute_RechecksBusinessAndFileState_ThenReportsBeforeAndAfter()
    {
        ApiTryArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.ChangeOnInvoke = true;
        var data = (ApiTryData)new ApiTryExecuteOperation(Planner(), _dispatch).Execute(args)!;
        Assert.Equal("before", data.Before["snapshot:1"]);
        Assert.Equal("after", data.After["snapshot:1"]);
        Assert.Equal("old-file", data.Before["file:Page.eod"]);
        Assert.Equal("new-file", data.After["file:Page.eod"]);
        Assert.Equal(1, _dispatch.Invocations);
    }

    [Fact]
    public void StalePlan_NeverInvokes()
    {
        ApiTryArgs args = Args(); args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.ChangeOnInvoke = true; _dispatch.Invoke(args);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new ApiTryExecuteOperation(Planner(), _dispatch).Execute(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(1, _dispatch.Invocations);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("modal")]
    public void UnverifiedInvocationFailure_IsUnknown_NotClaimedRolledBack(string cause)
    {
        ApiTryArgs args = Args(); args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.Failure = new InvalidOperationException(cause);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new ApiTryExecuteOperation(Planner(), _dispatch).Execute(args)).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Scratch", Path = @"D:\scratch\Scratch" }];
    }
    private sealed class Dispatcher : IApiTryDispatcher
    {
        public int Invocations { get; private set; }
        public int Inspections { get; private set; }
        public bool ChangeOnInvoke { get; set; }
        public Exception? Failure { get; set; }
        private bool _changed;
        public void CheckTarget(ApiTryArgs args) { }
        public Dictionary<string, string> Inspect(string project)
        {
            Inspections++;
            return new() { ["snapshot:1"] = _changed ? "after" : "before", ["file:Page.eod"] = _changed ? "new-file" : "old-file" };
        }
        public string Invoke(ApiTryArgs args)
        {
            Invocations++; if (Failure is { } failure) { throw failure; }
            _changed |= ChangeOnInvoke; return "=A/1";
        }
    }
}
