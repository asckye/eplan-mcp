using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using TaskStatus = EplanMcp.Host.Plans.TaskStatus;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanExecutionResultTests
{
    [Theory]
    [InlineData(ExecutionState.NotStarted, ExecutionState.NotStarted, PlanExecution.NotStarted)]
    [InlineData(ExecutionState.RolledBack, ExecutionState.NotStarted, PlanExecution.RolledBack)]
    [InlineData(ExecutionState.Committed, ExecutionState.NotStarted, PlanExecution.PartiallyCommitted)]
    [InlineData(ExecutionState.Committed, ExecutionState.RolledBack, PlanExecution.PartiallyCommitted)]
    [InlineData(ExecutionState.Committed, ExecutionState.Committed, PlanExecution.Committed)]
    [InlineData(ExecutionState.Committed, ExecutionState.Unknown, PlanExecution.Unknown)]
    public void SettledPrefix_AndUnsentTail_UseFiveValueTable(string first, string second, object expected)
    {
        PlanResult result = PlanResult.Aggregate([Step(1, first), Step(2, second), new(3, "page.update", false, ExecutionState.NotStarted)]);
        Assert.Equal(expected, result.Execution);
        Assert.False(result.Retryable);
    }

    [Theory]
    [InlineData(false, TaskStatus.Failed)]
    [InlineData(true, TaskStatus.Cancelled)]
    public void CancelledPartialRun_KeepsCommitFacts(bool cancelled, object expected)
    {
        PlanResult result = PlanResult.Aggregate([Step(1, ExecutionState.Committed), Step(2, ExecutionState.NotStarted)]) with { Verification = "failed" };
        Assert.Equal(expected, TaskRecord.StatusFor(result, cancelled, false));
        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
    }

    [Fact]
    public void Unknown_PrioritizesSuspensionOverCancellation()
    {
        PlanResult result = PlanResult.Aggregate([Step(1, ExecutionState.Committed), Step(2, ExecutionState.Unknown)]);
        Assert.Equal(TaskStatus.Unknown, TaskRecord.StatusFor(result, true, true));
        Assert.Equal(ErrorReason.OutcomeUnknown, result.Error!.Reason);
    }

    [Theory]
    [InlineData("page.create")]
    [InlineData("page.update")]
    [InlineData("page.delete")]
    [InlineData("page.renumber")]
    [InlineData("device.update")]
    [InlineData("device.renumber")]
    [InlineData("macro.insert")]
    public void SevenTypedEnvelopes_KeepFingerprintOutsideNestedArgs(string op)
    {
        PlanStep step = PlanVerificationTestData.Compile(op);
        PlanDefinition plan = PlanTestData.Definition() with { Project = PlanVerificationTestData.Baseline().Snapshot.Project, Steps = [step] };
        WritePlan fresh = PlanStepCompilationData.Plan(op);
        PlanStepExecuteArgs request = PlanStepRequest.Build(plan, step, fresh, [], new(PlanVerificationTestData.Baseline().Snapshot, true),
            op == "macro.insert" ? PlanPredictionTestData.Macro() : null);
        object nested = new object?[] { request.PageCreate, request.PageUpdate, request.PageDelete, request.PageRenumber,
            request.DeviceUpdate, request.DeviceRenumber, request.MacroInsert }.OfType<object>().Single();
        System.Text.Json.JsonElement args = System.Text.Json.JsonSerializer.SerializeToElement(nested, HostJson.Options);

        Assert.Equal(plan.Project.Path, args.GetProperty("project").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, args.GetProperty("fingerprint").ValueKind);
        Assert.Equal(fresh.Fingerprint, request.Fingerprint);
        Assert.Equal(fresh.Changes, request.ApprovedChanges);
        Assert.Equal(op == "macro.insert", request.MacroConstraint is not null);
    }

    [Fact]
    public async Task PredictedMacro_GuardCoversOriginalPagesAndCreatedPageAsOneMemberSet()
    {
        PlanStep creator = PlanPredictionTestData.Creation();
        PlanStep macro = await PlanPredictionTestData.PredictAsync();
        PlanBaseline baseline = PlanVerificationTestData.Baseline();
        PlanSnapshot after = PlanVerificationTestData.After("page.create", baseline.Snapshot);
        PlanDefinition plan = PlanTestData.Definition() with { Project = baseline.Snapshot.Project, Steps = [creator, macro] };
        PlanStepExecuteArgs request = PlanStepRequest.Build(plan, macro, PlanPredictionTestData.RealPlan(macro),
            [new(1, ExecutionState.Committed, new(baseline.Snapshot, true), new(after, true))], new(baseline.Snapshot, true), PlanPredictionTestData.Macro());
        string fullScope = System.Text.Json.JsonSerializer.Serialize(request.Guard.ReadScope, HostJson.Options);
        PlanGuardCondition complete = Assert.Single(request.Guard.Preconditions,
            c => c.Tag == "members" && System.Text.Json.JsonSerializer.Serialize(c.Scope, HostJson.Options) == fullScope);

        Assert.Equal(after.MembersHash, complete.Hash);
        Assert.Contains(PlanPredictionTestData.Page, request.Guard.ReadScope.Pages);
        Assert.True(request.Guard.Preconditions.Count(c => c.Tag == "members") >= 2);
    }

    private static PlanStepResult Step(int index, string execution) => new(index, "page.update", true, execution,
        execution == ExecutionState.Unknown ? new(ErrorReason.OutcomeUnknown, "Check the project.", false) : null);
}
