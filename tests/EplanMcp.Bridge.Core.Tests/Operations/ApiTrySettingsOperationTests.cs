using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTrySettingsOperationTests
{
    private const string Prefix = "O:Eplan.EplApi.";
    private readonly Dispatcher _dispatch = new();
    private readonly FakeTransactions _transactions = new();
    private ApiTryPlanOperation Planner() => new(2026, new Catalog(), _dispatch);
    private static ApiTryArgs Args(string member = "Base.SettingNode.GetListOfNodes") => new()
    {
        Group = Prefix + member,
        Project = "Scratch",
        Parameters = new JObject
        {
            ["target"] = new JObject { ["project"] = @"D:\scratch\Scratch", ["page"] = "", ["type"] = 1, ["oid"] = 1, ["digest"] = new string('a', 64) },
            ["node"] = "USER",
            ["offset"] = 0,
            ["limit"] = 10,
        }.ToString(),
    };
    private WritePlan Plan(ApiTryArgs args) => (WritePlan)Planner().Execute((object)args)!;

    [Theory]
    [InlineData("node", "COMPANY.X")]
    [InlineData("node", "USER..X")]
    [InlineData("limit", "201")]
    [InlineData("offset", "-1")]
    public void BridgeRejectsInvalidScopeAndPagingBeforeNativeValidation(string name, string value)
    {
        ApiTryArgs args = Args();
        JObject p = JObject.Parse(args.Parameters);
        p[name] = name == "node" ? new JValue(value) : new JValue(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        args.Parameters = p.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Equal(0, _dispatch.Checks);
    }

    [Theory]
    [InlineData("Base.Settings.SetStringSetting")]
    [InlineData("Base.SchemeSetting.SetScheme")]
    [InlineData("Base.SettingNode.ResetNode")]
    [InlineData("DataModel.ProjectSettings.ReadSettings")]
    [InlineData("DataModel.ProjectSettingNode.Set")]
    [InlineData("DataModel.ProjectSchemeSetting.Init")]
    public void SettingsWritesNeverReachTheDispatcher(string member)
    {
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Plan(Args(member))).Error.Reason);
        Assert.Equal(0, _dispatch.Checks);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("wrong-project")]
    [InlineData("bad-target")]
    public void SetterAndForgedLocatorsAreRejected(string mode)
    {
        ApiTryArgs args = Args();
        JObject p = JObject.Parse(args.Parameters);
        if (mode == "value")
        { p["value"] = "write"; }
        if (mode == "wrong-project")
        { p["target"]!["project"] = @"D:\real\Project"; }
        if (mode == "bad-target")
        { p["target"]!["digest"] = "forged"; }
        args.Parameters = p.ToString();
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        Assert.Equal(0, _dispatch.Checks);
    }

    [Fact]
    public void SettingsReadUsesFreshValidationAndSnapshotsWithoutUndoOrCurrentProjectDependency()
    {
        ApiTryArgs args = Args();
        WritePlan plan = Plan(args);
        Assert.Equal("read", Assert.Single(plan.Changes).Action);
        args.Fingerprint = plan.Fingerprint;
        var result = (ApiTryData)new ApiTryExecuteOperation(Planner(), _dispatch, _transactions).Execute(args)!;
        Assert.Equal("{\"items\":[\"A\"],\"total\":1,\"nextOffset\":null}", result.Value);
        Assert.Equal(2, _dispatch.Checks);
        Assert.Equal(3, _dispatch.Inspections);
        Assert.Equal(1, _dispatch.Calls);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void ProjectSettingsGetterWithOnlyProjectTargetReadsWithoutUndoStep()
    {
        ApiTryArgs args = Args("DataModel.Project.Settings");
        args.Parameters = new JObject { ["target"] = JObject.Parse(args.Parameters)["target"] }.ToString();
        WritePlan plan = Plan(args);
        Assert.Equal("dispatchable", plan.Details!["status"]);
        Assert.Equal("read", Assert.Single(plan.Changes).Action);
        args.Fingerprint = plan.Fingerprint;
        var result = (ApiTryData)new ApiTryExecuteOperation(Planner(), _dispatch, _transactions).Execute(args)!;
        Assert.Equal("PROJECT", result.Value);
        Assert.Equal(result.Before, result.After);
        Assert.Equal(2, _dispatch.Checks);
        Assert.Equal(1, _dispatch.Calls);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefusedSettingsReadPreservesKnownFailureAndUnknownRules(bool changed)
    {
        ApiTryArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.Refuse = true;
        _dispatch.ChangeOnInvoke = changed;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new ApiTryExecuteOperation(Planner(), _dispatch, _transactions).Execute(args)).Error;
        Assert.Equal(changed ? ErrorReason.OutcomeUnknown : ErrorReason.EplanOperationFailed, error.Reason);
        Assert.Equal(changed ? ExecutionState.Unknown : ExecutionState.RolledBack, error.Execution);
        Assert.Empty(_transactions.Calls);
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Scratch", Path = @"D:\scratch\Scratch", Current = false }];
    }
    private sealed class Dispatcher : IApiTryDispatcher
    {
        public int Checks { get; private set; }
        public int Calls { get; private set; }
        public int Inspections { get; private set; }
        public bool Refuse { get; set; }
        public bool ChangeOnInvoke { get; set; }
        public void CheckTarget(ApiTryArgs args) => Checks++;
        public string? Read(ApiTryArgs args) => throw new InvalidOperationException();
        public object? Invoke(ApiTryArgs args)
        {
            Calls++;
            if (Refuse)
            { throw new InvalidOperationException("read refused"); }
            return args.Group == Prefix + "DataModel.Project.Settings" ? "PROJECT" : "{\"items\":[\"A\"],\"total\":1,\"nextOffset\":null}";
        }
        public Dictionary<string, string> Inspect(ApiTryArgs args)
        { Inspections++; return new() { ["snapshot:1"] = Calls > 0 && ChangeOnInvoke ? "changed" : "before" }; }
    }
}
