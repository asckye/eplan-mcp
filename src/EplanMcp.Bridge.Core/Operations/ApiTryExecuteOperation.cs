using EplanMcp.Contracts;
using Newtonsoft.Json;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Outcome-tracked trials; writes share one SafetyPoint and undo step with their observations.</summary>
public sealed class ApiTryExecuteOperation(ApiTryPlanOperation planner, IApiTryDispatcher dispatch, ITransactionScopeFactory transactions) : Operation<ApiTryArgs, ApiTryData>, IWriteOperation
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
        bool write = plan.Changes[0].Action == "write";
        try
        {
            if (!write)
            {
                try
                { return Invoke(); }
                catch (Exception ex) when (ex is not (BridgeErrorException or TimeoutException or OperationCanceledException))
                {
                    // EPLAN refuses some reads (e.g. S029128, no current page): unchanged state makes that a known failure.
                    var before = JsonConvert.DeserializeObject<Dictionary<string, string>>(plan.Details!["before"])!;
                    Dictionary<string, string> after = dispatch.Inspect(args.Project);
                    if (before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out string? v) && v == p.Value))
                    {
                        throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused this read and nothing changed; it may need a current page or selection.", ex)
                        { Error = { Execution = ExecutionState.RolledBack } };
                    }

                    throw;
                }
            }
            Committed<ApiTryData> committed = TransactionRunner.Run(transactions, "eplan_api_try", () =>
            {
                try
                { return Invoke(); }
                catch (BridgeErrorException) { throw; }
                catch (TimeoutException) { throw; }
                catch (Exception ex) { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The API trial failed and was rolled back; re-read before retrying.", ex); }
            });
            committed.Result.Warning = committed.Warning;
            return committed.Result;
        }
        catch (BridgeErrorException ex) when (ex.Error.Execution == ExecutionState.RolledBack
            && ex.Error.Reason is not (ErrorReason.Timeout or ErrorReason.EplanBusy or ErrorReason.OutcomeUnknown))
        { throw; }
        catch (Exception ex)
        { throw new BridgeErrorException(ErrorReason.OutcomeUnknown, "Unverified API outcome is unknown. Do not repeat; ask the engineer to inspect EPLAN.", ex) { Error = { Execution = ExecutionState.Unknown } }; }

        ApiTryData Invoke()
        {
            var before = JsonConvert.DeserializeObject<Dictionary<string, string>>(plan.Details!["before"])!;
            string key = "property:" + args.Group;
            bool setter = write && ApiTryCatalog.MethodAccess(args.Group) is null;
            if (setter)
            { before[key] = JsonConvert.SerializeObject(dispatch.Read(args)); }
            object? result = dispatch.Invoke(args);
            string? value = result as string;
            if (setter && value != Convert.ToString(((Newtonsoft.Json.Linq.JValue)ApiTryPlanOperation.Parameters(args.Parameters)["value"]!).Value, System.Globalization.CultureInfo.InvariantCulture))
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The property did not read back as requested; the trial was rolled back."); }
            OperationContext.Current.ThrowIfCancellationRequested();
            var data = new ApiTryData { Value = value, Locator = result as DrawingLocator, Before = before, After = dispatch.Inspect(args.Project) };
            if (setter)
            { data.After[key] = JsonConvert.SerializeObject(value); }
            OperationContext.Current.ThrowIfCancellationRequested();
            if (elapsed.Elapsed.TotalSeconds > 30)
            { throw new TimeoutException("Trial exceeded its execution budget."); }
            return data;
        }
    }
}
