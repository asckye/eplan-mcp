using EplanMcp.Contracts;
using Newtonsoft.Json;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Read-only first slice, still outcome-tracked because unverified calls may have side effects.</summary>
public sealed class ApiTryExecuteOperation(ApiTryPlanOperation planner, IApiTryDispatcher dispatch) : Operation<ApiTryArgs, ApiTryData>, IWriteOperation
{
    public override string Name => OperationNames.ApiTryExecute;
    protected override ApiTryData Execute(ApiTryArgs args)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        WritePlan plan;
        try
        {
            plan = planner.Prepare(args);
            if (plan.Changes.Count == 0 || string.IsNullOrEmpty(args.Fingerprint) || args.Fingerprint != plan.Fingerprint)
            { throw new BridgeErrorException(ErrorReason.PlanStale, "The trial plan changed; request a new plan."); }
            OperationContext.Current.ThrowIfCancellationRequested();
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        try
        {
            string? value = dispatch.Invoke(args);
            OperationContext.Current.ThrowIfCancellationRequested();
            var data = new ApiTryData { Value = value, Before = JsonConvert.DeserializeObject<Dictionary<string, string>>(plan.Details!["before"])!, After = dispatch.Inspect(args.Project) };
            OperationContext.Current.ThrowIfCancellationRequested();
            if (elapsed.Elapsed.TotalSeconds > 30) { throw new TimeoutException("Trial exceeded its execution budget."); }
            return data;
        }
        catch (Exception ex)
        { throw new BridgeErrorException(ErrorReason.OutcomeUnknown, "Unverified API outcome is unknown. Do not repeat; ask the engineer to inspect EPLAN.", ex) { Error = { Execution = ExecutionState.Unknown } }; }
    }
}
