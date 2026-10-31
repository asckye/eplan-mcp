using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTryOperationTests
{
    private const string Group = "O:Eplan.EplApi.DataModel.Page.Name";
    private readonly Dispatcher _dispatch = new();
    private readonly FakeTransactions _transactions = new();
    private readonly Catalog _catalog = new();
    private ApiTryExecuteOperation Executor() => new(Planner(), _dispatch, _transactions);
    private ApiTryPlanOperation Planner(int major = 2026) => new(major, _catalog, _dispatch);
    private static ApiTryArgs Args(string group = Group) => new()
    {
        Group = group,
        Project = "Scratch",
        Parameters = new JObject
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
    [InlineData("unknown-field")]
    [InlineData("duplicate")]
    [InlineData("array")]
    public void BadShapes_AreRejectedWithoutInvoking(string mode)
    {
        ApiTryArgs args = Args();
        JObject p = JObject.Parse(args.Parameters);
        var locator = (JObject)p["target"]!;
        switch (mode)
        {
            case "missing":
                p.Remove("target");
                break;
            case "type-string":
                locator["type"] = "1";
                break;
            case "digest":
                locator["digest"] = new string('A', 64);
                break;
            case "wrong-project":
                locator["project"] = @"D:\real\Project";
                break;
            case "extra-locator-field":
                locator["typeName"] = "System.IO.File";
                break;
            case "unknown-field":
                p["unknown"] = "new name";
                break;
            case "array":
                p["target"] = new JArray(locator);
                break;
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
        if (valid)
        { Assert.Single(Plan(args).Changes); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason); }
    }

    [Theory]
    [InlineData("Function.IsPointInsideLogicalArea", "pnt", "{\"x\":1.25,\"y\":-2}", true)]
    [InlineData("Function.IsPointInsideLogicalArea", "pnt", "{\"x\":1,\"y\":\"2\"}", false)]
    [InlineData("Function.IsPointInsideLogicalArea", "pnt", "{\"x\":1,\"y\":1e999}", false)]
    [InlineData("Function.IsPointInsideLogicalArea", "pnt", "{\"x\":1,\"y\":2,\"z\":3}", false)]
    [InlineData("Function.IsPointInsideLogicalArea", "pnt", "{\"x\":1,\"x\":2,\"y\":3}", false)]
    [InlineData("Page.GetWriteProtectionFlagSet", "nKind", "\"Manual\"", true)]
    [InlineData("Page.GetWriteProtectionFlagSet", "nKind", "\"manual\"", false)]
    [InlineData("Page.GetWriteProtectionFlagSet", "nKind", "\"2\"", false)]
    [InlineData("Page.GetWriteProtectionFlagSet", "nKind", "2", false)]
    [InlineData("Page.GetWriteProtectionFlagSet", "value", "\"Manual\"", false)]
    [InlineData("Graphics.PlaceHolder.ApplyRecord", "strRecordName", "\"test\"", true)]
    public void MethodParametersRequireExactGeneratedTypes(string member, string name, string json, bool valid)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel." + member);
        args.Parameters = args.Parameters[..^1] + ",\"" + name + "\":" + json + "}";
        if (valid)
        { Assert.Single(Plan(args).Changes); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason); }
        Assert.Equal(0, _dispatch.Invocations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocatorArgumentsMustBelongToTheTargetProject(bool sameProject)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Function.Assign");
        JObject p = JObject.Parse(args.Parameters);
        p["argument_target"] = p["target"]!.DeepClone();
        if (!sameProject)
        { p["argument_target"]!["project"] = @"D:\real\Project"; }
        args.Parameters = p.ToString();
        if (sameProject)
        { Assert.Equal("write", Assert.Single(Plan(args).Changes).Action); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason); }
    }

    [Fact]
    public void MethodWriteReturnsNullOrLocatorWithoutSetterReadback_AndCommitsOneUndoStep()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Function.Assign");
        JObject p = JObject.Parse(args.Parameters);
        p["argument_target"] = p["target"]!.DeepClone();
        args.Parameters = p.ToString();
        foreach (object? returned in new object?[] { null, p["target"]!.ToObject<DrawingLocator>() })
        {
            _transactions.Calls.Clear();
            _dispatch.MethodResult = returned;
            args.Fingerprint = Plan(args).Fingerprint;
            var result = (ApiTryData)Executor().Execute(args)!;
            Assert.Null(result.Value);
            Assert.Same(returned, result.Locator);
            Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
            Assert.DoesNotContain(result.After.Keys, k => k.StartsWith("property:", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodWriteFailureRollsBackAndTimeoutRemainsUnknown(bool timeout)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Graphics.PlaceHolder.AddRecord");
        JObject p = JObject.Parse(args.Parameters);
        p["strRecordName"] = "test";
        args.Parameters = p.ToString();
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.Failure = timeout ? new TimeoutException() : new InvalidOperationException();
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal(timeout ? ExecutionState.Unknown : ExecutionState.RolledBack, error.Execution);
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void MethodReadRunsOnceWithoutCreatingAnUndoStep()
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Page.GetWriteProtectionFlagSet");
        JObject p = JObject.Parse(args.Parameters);
        p["nKind"] = "Manual";
        args.Parameters = p.ToString();
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.MethodResult = "True";
        Assert.Equal("True", ((ApiTryData)Executor().Execute(args)!).Value);
        Assert.Equal(1, _dispatch.Invocations);
        Assert.Empty(_transactions.Calls);
    }

    private static ApiTryArgs ServiceArgs(bool write)
    {
        ApiTryArgs args = Args(write ? "O:Eplan.EplApi.HEServices.ConnectionService.SwapSourceAndTarget" : "O:Eplan.EplApi.HEServices.SelectionSet.GetSelectedObject");
        JObject p = JObject.Parse(args.Parameters);
        if (write)
        { p["connection"] = p["target"]!.DeepClone(); }
        else
        { p["bFirst"] = true; }
        args.Parameters = p.ToString();
        return args;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServiceReadAndWriteUseExistingObservationsAndExactlyOneWriteTransaction(bool write)
    {
        ApiTryArgs args = ServiceArgs(write);
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.ChangeOnInvoke = write;
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Equal("before", data.Before["snapshot:1"]);
        Assert.Equal(write ? "after" : "before", data.After["snapshot:1"]);
        Assert.Equal(1, _dispatch.Invocations);
        Assert.Equal(write ? ["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"] : Array.Empty<string>(), _transactions.Calls);
    }

    [Fact]
    public void ServiceLocatorInAnotherScratchProjectIsRefusedBeforeInspectionAndExecution()
    {
        ApiTryArgs args = ServiceArgs(true);
        JObject p = JObject.Parse(args.Parameters);
        p["connection"]!["project"] = @"D:\scratch\Another";
        args.Parameters = p.ToString();
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(0, _dispatch.Inspections);
        Assert.Equal(0, _dispatch.Invocations);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void ImplicitServiceRequiresCurrentTargetAndRechecksItAtExecution()
    {
        ApiTryArgs args = ServiceArgs(false);
        args.Fingerprint = Plan(args).Fingerprint;
        _catalog.Current = false;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(0, _dispatch.Invocations);
        Assert.Empty(_transactions.Calls);
        Assert.Equal("write", Assert.Single(Plan(ServiceArgs(true)).Changes).Action);
    }

    [Fact]
    public void ServiceWriteFailureRollsBackWithoutCommitting()
    {
        ApiTryArgs args = ServiceArgs(true);
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.Failure = new InvalidOperationException();
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error.Execution);
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Theory]
    [InlineData("System.Int32", "Integer", "2147483648", false)]
    [InlineData("System.Int64", "Integer", "9223372036854775807", true)]
    [InlineData("System.Int64", "Integer", "9223372036854775808", false)]
    [InlineData("System.Int64", "Float", "1.0", false)]
    [InlineData("System.Double", "Float", "NaN", false)]
    public void GeneratedScalarValidationRejectsOverflowAndCoercion(string type, string kind, string value, bool valid) => Assert.Equal(valid, ApiTryCatalog.ValidValue([type], kind, value));

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
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Equal("before", data.Before["snapshot:1"]);
        Assert.Equal("after", data.After["snapshot:1"]);
        Assert.Equal("old-file", data.Before["file:Page.eod"]);
        Assert.Equal("new-file", data.After["file:Page.eod"]);
        Assert.Equal(1, _dispatch.Invocations);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void StalePlan_NeverInvokes()
    {
        ApiTryArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.ChangeOnInvoke = true;
        _dispatch.Invoke(args);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(1, _dispatch.Invocations);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("modal")]
    public void UnverifiedInvocationFailure_IsUnknown_NotClaimedRolledBack(string cause)
    {
        ApiTryArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        _dispatch.Failure = new InvalidOperationException(cause);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Theory]
    [InlineData("Page.Name", "\"new name\"", true)]
    [InlineData("Page.Name", "true", false)]
    [InlineData("Page.WriteProtected", "true", true)]
    [InlineData("Page.WriteProtected", "\"true\"", false)]
    [InlineData("Page.GridSize", "2.5", true)]
    [InlineData("Page.GridSize", "\"2.5\"", false)]
    [InlineData("Page.GridSize", "1e999", false)]
    [InlineData("MacroBox.Variant", "1", true)]
    [InlineData("MacroBox.Variant", "1.5", false)]
    [InlineData("MacroBox.Variant", "2147483648", false)]
    [InlineData("Page.PageType", "\"Circuit\"", true)]
    [InlineData("Page.PageType", "\"circuit\"", false)]
    [InlineData("Page.PageType", "1", false)]
    [InlineData("Page.PageTypeName", "\"new\"", false)]
    public void Setters_RequireExactScalarTypeOrPublishedEnumName(string member, string value, bool valid)
    {
        ApiTryArgs args = Args("O:Eplan.EplApi.DataModel." + member);
        JObject parameters = JObject.Parse(args.Parameters);
        parameters["value"] = JToken.Parse(value);
        args.Parameters = parameters.ToString();
        if (valid)
        {
            PlannedChange change = Assert.Single(Plan(args).Changes);
            Assert.Equal(("write", value), (change.Action, change.NewValue));
        }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason); }
        Assert.Equal(0, _dispatch.Invocations);
    }

    private ApiTryArgs WriteArgs()
    {
        ApiTryArgs args = Args();
        JObject p = JObject.Parse(args.Parameters);
        p["value"] = "new name";
        args.Parameters = p.ToString();
        args.Fingerprint = Plan(args).Fingerprint;
        _transactions.Snapshot = () => { string before = _dispatch.Value; return () => _dispatch.Value = before; };
        return args;
    }

    [Fact]
    public void DoubleSetter_ValidatesInvariantJsonInGermanCulture()
    {
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            ApiTryArgs args = Args("O:Eplan.EplApi.DataModel.Page.GridSize");
            JObject p = JObject.Parse(args.Parameters);
            p["value"] = 2.5;
            args.Parameters = p.ToString();
            Assert.Equal("2.5", Assert.Single(Plan(args).Changes).NewValue);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void Write_ReadsBackAndCommitsExactlyOneStep_WithPropertyBeforeAfter()
    {
        ApiTryArgs args = WriteArgs();
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Equal("new name", data.Value);
        Assert.Equal("\"=A/1\"", data.Before["property:" + Group]);
        Assert.Equal("\"new name\"", data.After["property:" + Group]);
        Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Write_ExceptionOrReadbackMismatch_RollsBack(bool mismatch)
    {
        ApiTryArgs args = WriteArgs();
        _dispatch.Mismatch = mismatch;
        _dispatch.Failure = mismatch ? null : new InvalidOperationException("setter failed after writing");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal("=A/1", _dispatch.Value);
        Assert.Equal(["Begin AI: eplan_api_try", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData(ErrorReason.Timeout)]
    [InlineData(ErrorReason.EplanBusy)]
    public void Write_TimeoutOrModal_RemainsUnknownEvenAfterRollback(string reason)
    {
        ApiTryArgs args = WriteArgs();
        _dispatch.Failure = new BridgeErrorException(reason, "blocked");
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Theory]
    [InlineData("Commit")]
    [InlineData("Rollback")]
    public void Write_CommitOrRollbackFailure_IsUnknown(string step)
    {
        ApiTryArgs args = WriteArgs();
        _transactions.FailAt = step;
        if (step == "Rollback")
        { _dispatch.Mismatch = true; }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Reason, error.Execution));
    }

    [Fact]
    public void Write_UndoCloseFailure_PreservesCommittedWarning()
    {
        ApiTryArgs args = WriteArgs();
        _transactions.FailAt = "CloseUndo";
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Contains("Ctrl+Z", data.Warning, StringComparison.Ordinal);
    }

    private sealed class Catalog : IProjectCatalog
    {
        public bool Current { get; set; } = true;
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Scratch", Path = @"D:\scratch\Scratch", Current = Current }];
    }
    private sealed class Dispatcher : IApiTryDispatcher
    {
        public int Invocations { get; private set; }
        public int Inspections { get; private set; }
        public bool ChangeOnInvoke { get; set; }
        public Exception? Failure { get; set; }
        public bool Mismatch { get; set; }
        public string Value { get; set; } = "=A/1";
        public object? MethodResult { get; set; }
        private bool _changed;
        public void CheckTarget(ApiTryArgs args) { }
        public Dictionary<string, string> Inspect(string project)
        {
            Inspections++;
            return new() { ["snapshot:1"] = _changed ? "after" : "before", ["file:Page.eod"] = _changed ? "new-file" : "old-file" };
        }
        public string Read(ApiTryArgs args) => Value;
        public object? Invoke(ApiTryArgs args)
        {
            Invocations++;
            if (JObject.Parse(args.Parameters).TryGetValue("value", out JToken? value))
            { Value = Mismatch ? "different" : value.Value<string>()!; }
            if (Failure is { } failure)
            { throw failure; }
            _changed |= ChangeOnInvoke;
            if (ApiTryCatalog.MethodAccess(args.Group) is not null)
            { return MethodResult; }
            return Value;
        }
    }
}
