using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTryLifecycleTests
{
    private const string Project = @"D:\scratch\Scratch";
    private const string Factory = "O:Eplan.EplApi.DataModel.Graphics.Line.Create";
    private readonly FakeTransactions _transactions = new();
    private readonly Dispatcher _dispatch = new();
    private ApiTryPlanOperation Planner() => new(2026, new Catalog(), _dispatch);
    private JObject Target() => JObject.FromObject(new { project = Project, page = "=A/1", type = 1, oid = 2, digest = _dispatch.Locator.Digest });
    private ApiTryArgs Args(bool remove = false) => new()
    {
        Project = Project,
        Group = remove ? "O:Eplan.EplApi.DataModel.Page.Remove" : Factory,
        Parameters = (remove ? new JObject { ["target"] = Target() }
            : new JObject { ["page"] = Target(), ["pntStart"] = new JObject { ["x"] = 0, ["y"] = 0 }, ["pntEnd"] = new JObject { ["x"] = 4, ["y"] = 4 } }).ToString(),
    };
    private ApiTryArgs Planned(bool remove = false)
    {
        ApiTryArgs args = Args(remove);
        args.Fingerprint = ((WritePlan)Planner().Execute(args)!).Fingerprint;
        return args;
    }
    private ApiTryData Execute(ApiTryArgs args) => (ApiTryData)new ApiTryExecuteOperation(Planner(), _dispatch, _transactions).Execute(args)!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LifecycleWritesRecordIdentitiesAndRefreshOnlyAfterSingleCommit(bool remove)
    {
        _dispatch.Remove = remove;
        _dispatch.Exists = remove;
        _dispatch.OnRefresh = () => Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        ApiTryData data = Execute(Planned(remove));
        Assert.True(data.UndoExpected);
        Assert.Equal(1, _dispatch.Invocations);
        Assert.Equal(remove, data.Before.ContainsKey("object:line:2"));
        Assert.Equal(!remove, data.After.ContainsKey("object:line:2"));
        if (!remove)
        { Assert.Equal(new string('b', 64), data.Locator!.Digest); }
        else
        { Assert.Null(data.Locator); Assert.Contains("removed line", data.Before["object:line:2"], StringComparison.Ordinal); }
    }
    [Fact]
    public void FactoryPlanningDoesNotConstruct_AndRecordsEffect()
    {
        WritePlan plan = (WritePlan)Planner().Execute(Args())!;
        Assert.Equal("write", Assert.Single(plan.Changes).Action);
        Assert.Equal("creates", plan.Details!["lifecycle"]);
        Assert.Equal(0, _dispatch.Invocations);
    }
    [Theory]
    [InlineData("Graphics.Line", false)]
    [InlineData("Graphics.Line", true)]
    [InlineData("ProtectedGroup", false)]
    public void FactoryWithExistingReceiverIsRejectedBeforePlanningOrExecution(string type, bool handle)
    {
        ApiTryArgs args = Args();
        args.Group = "O:Eplan.EplApi.DataModel." + type + ".Create";
        JObject parameters = JObject.Parse(args.Parameters);
        parameters["target"] = handle ? JObject.FromObject(new { handle = "h:" + new string('a', 32), type = "Eplan.EplApi.DataModel.Graphics.Line" }) : Target();
        args.Parameters = parameters.ToString();
        BridgeError planned = Assert.Throws<BridgeErrorException>(() => Planner().Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (planned.Reason, planned.Execution));
        Assert.Contains("Omit target for factory creation", planned.Message, StringComparison.Ordinal);
        BridgeError executed = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (executed.Reason, executed.Execution));
        Assert.Equal(0, _dispatch.Checks);
        Assert.Equal(0, _dispatch.Observations);
        Assert.Equal(0, _dispatch.Invocations);
        Assert.Empty(_transactions.Calls);
    }
    [Theory]
    [InlineData("extra")]
    [InlineData("wrong-project")]
    [InlineData("nonfinite")]
    [InlineData("missing")]
    public void FactoryValidationRefusesUnapprovedShapes(string mode)
    {
        ApiTryArgs args = Args();
        JObject parameters = JObject.Parse(args.Parameters);
        if (mode == "extra")
        { parameters["x"] = 1; }
        if (mode == "wrong-project")
        { parameters["page"]!["project"] = @"D:\real\Project"; }
        if (mode == "nonfinite")
        { parameters["pntStart"]!["x"] = "NaN"; }
        if (mode == "missing")
        { parameters.Remove("page"); }
        args.Parameters = parameters.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Planner().Execute(args)).Error.Reason);
        Assert.Equal(0, _dispatch.Invocations);
    }
    [Fact]
    public void ChangedLifecycleObservationInvalidatesApproval()
    {
        ApiTryArgs args = Planned();
        _dispatch.Exists = true;
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_transactions.Calls);
    }
    [Fact]
    public void FailureBeforeCommitRollsBack_AndPostCommitReadbackFailureIsUnknown()
    {
        ApiTryArgs args = Planned();
        _dispatch.FailInvoke = true;
        _transactions.Snapshot = () => () => _dispatch.Exists = false;
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Execution);
        Assert.False(_dispatch.Exists);
        Assert.DoesNotContain("Commit", _transactions.Calls);
        _transactions.Calls.Clear();
        _dispatch.FailInvoke = false;
        _dispatch.OnRefresh = () => throw new InvalidOperationException("missing after commit");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Planned())).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        Assert.DoesNotContain("Rollback", _transactions.Calls);
        Assert.Single(_transactions.Calls, c => c == "Commit");
    }
    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Scratch", Path = Project, Current = true }];
    }
    private sealed class Dispatcher : IApiTryDispatcher, IApiTryLifecycleDispatcher
    {
        public DrawingLocator Locator { get; } = new() { Project = Project, Page = "=A/1", Type = 1, Oid = 2, Digest = new string('a', 64) };
        public bool Remove { get; set; }
        public bool Exists { get; set; }
        public bool FailInvoke { get; set; }
        public int Invocations { get; private set; }
        public int Checks { get; private set; }
        public int Observations { get; private set; }
        public Action? OnRefresh { get; set; }
        public void CheckTarget(ApiTryArgs args) { Checks++; }
        public string? Read(ApiTryArgs args) => null;
        public Dictionary<string, string> Inspect(ApiTryArgs args) => Observe(args);
        public Dictionary<string, string> Observe(ApiTryArgs args) { Observations++; return Exists ? new() { ["object:line:2"] = "removed line" } : []; }
        public DrawingLocator Refresh(string project, DrawingLocator locator)
        {
            OnRefresh?.Invoke();
            return new() { Project = locator.Project, Page = locator.Page, Type = locator.Type, Oid = locator.Oid, Digest = new string('b', 64) };
        }
        public object? Invoke(ApiTryArgs args)
        {
            Invocations++;
            Exists = !Remove;
            if (FailInvoke)
            { throw new InvalidOperationException("write failed"); }
            return Remove ? null : Locator;
        }
    }
}
