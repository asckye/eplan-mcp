using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ActionTryOperationTests
{
    private readonly OpenProject _project = new() { Name = "Scratch", Path = @"D:\scratch\copy", Current = true };
    private readonly Runner _runner = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Messages _messages = new();
    private static ActionTryArgs Args() => new() { Project = "Scratch", Action = "XViewAction", Type = "DETAIL", Parameters = [new() { Name = "Factor", Value = "1.25" }] };
    private WritePlan Plan(ActionTryArgs args) => (WritePlan)new ActionTryPlanOperation(new FakeCatalog(_project), _runner).Execute(args)!;
    private ActionRunData Execute(ActionTryArgs args) => (ActionRunData)new ActionTryExecuteOperation(new FakeCatalog(_project), _runner, _messages, _transactions).Execute(args)!;

    [Fact]
    public void Trial_UsesTypedParametersTransactionsAndFullFingerprintReadback()
    {
        var args = Args();
        var plan = Plan(args);
        Assert.Equal("true", plan.Details!["unverified"]);
        Assert.Contains("unverified", plan.Details["effects"], StringComparison.Ordinal);
        args.Fingerprint = plan.Fingerprint;
        var result = Execute(args);
        Assert.NotNull(result.After);
        Assert.Equal(result.Before, result.After);
        Assert.Equal(["Begin AI: eplan_action_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal("XViewAction", _runner.Action);
        Assert.True(_runner.Unverified);
        Assert.Equal("DETAIL", _runner.Parameters!["TYPE"]);
        Assert.Equal("1.25", _runner.Parameters["Factor"]);
        Assert.Equal(@"D:\scratch\copy", result.Target);
    }

    [Fact]
    public void Fingerprint_BindsParametersTypeActionAndDesignFiles()
    {
        foreach (string field in new[] { "parameter", "type", "action", "design" })
        {
            var args = Args();
            args.Fingerprint = Plan(args).Fingerprint;
            switch (field)
            {
                case "parameter": args.Parameters[0].Value = "2"; break;
                case "type": args.Type = "OTHER"; break;
                case "action": args.Action = "XOtherView"; break;
                case "design": _runner.Bytes++; break;
            }
            var error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
            Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        }
        Assert.Empty(_transactions.Calls);
        Assert.Null(_runner.Action);
    }

    [Fact]
    public void Trial_CannotTargetNonCurrentOrAdditionalProjects()
    {
        var args = Args();
        _project.Current = false;
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        _project.Current = true;
        var operation = new ActionTryPlanOperation(new FakeCatalog(_project, new() { Name = "Real", Path = @"D:\real" }), _runner);
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error.Reason);
        Assert.Equal(0, _runner.Inspections);
    }

    [Theory]
    [InlineData("ProjectOpen")]
    [InlineData("xsettingsimport")]
    [InlineData("changelayer")]
    [InlineData("CHANGELAYER")]
    [InlineData("ProjectAction /Action:Other")]
    public void Bridge_IndependentlyRejectsExcludedWhitelistAndCommandLineNames(string action)
    {
        var args = Args();
        args.Action = action;
        args.Type = null;
        var error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
        Assert.Equal(0, _runner.Inspections);
    }

    [Fact]
    public void Bridge_IndependentlyChecksTheTypeAgainstDenyData()
    {
        var args = Args();
        args.Type = "EXIT";
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Equal(0, _runner.Inspections);
    }

    [Theory]
    [InlineData("PROJECTNAME", "Scratch")]
    [InlineData("TYPE", "OTHER")]
    [InlineData("Factor", "1 /Project:Real")]
    [InlineData("Factor", "\" /Action:Exit")]
    public void Bridge_RejectsParameterInjectionBeforeInspecting(string name, string value)
    {
        var args = Args();
        args.Parameters = [new() { Name = name, Value = value }];
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Equal(0, _runner.Inspections);
        Assert.Empty(_transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunnerFailure_RollsBack_AndModalRemainsUnknown(bool modal)
    {
        var args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _runner.Fail = modal ? ErrorReason.OutcomeUnknown : ErrorReason.EplanOperationFailed;
        var error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal(modal ? ErrorReason.OutcomeUnknown : ErrorReason.EplanOperationFailed, error.Reason);
        Assert.Equal(modal ? ExecutionState.Unknown : ExecutionState.RolledBack, error.Execution);
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    private sealed class Messages : ISystemMessageReader
    {
        public IReadOnlyList<SystemMessageRecord> ReadAll() => [];
    }
    private sealed class Runner : IActionRunner
    {
        public string? Action { get; private set; }
        public bool Unverified { get; private set; }
        public int Inspections { get; private set; }
        public int Bytes { get; set; } = 10;
        public string? Fail { get; set; }
        public IReadOnlyDictionary<string, string>? Parameters { get; private set; }
        public ActionRunData Inspect(string entry, string? projectPath, IReadOnlyDictionary<string, string> parameters)
        {
            Inspections++;
            return new()
            {
                Target = projectPath!, Parameters = [.. parameters.Select(p => new ActionParameter { Name = p.Key, Value = p.Value })],
                Before = new() { ["count:pages"] = "1", ["file:Design.eod"] = Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\t2026-10-30T00:00:00Z" },
            };
        }
        public bool Run(string action, IReadOnlyDictionary<string, string> parameters, bool unverified = false)
        {
            Action = action; Parameters = parameters; Unverified = unverified;
            if (Fail is not null) { throw new BridgeErrorException(Fail, "Action failed."); }
            return true;
        }
    }
}
