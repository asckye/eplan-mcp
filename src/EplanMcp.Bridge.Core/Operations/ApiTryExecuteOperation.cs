using EplanMcp.Contracts;
using Newtonsoft.Json;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Outcome-tracked trials; writes share one SafetyPoint and undo step with their observations.</summary>
public sealed class ApiTryExecuteOperation(ApiTryPlanOperation planner, IApiTryDispatcher dispatch, ITransactionScopeFactory transactions) : Operation<ApiTryArgs, ApiTryData>, IWriteOperation
{
    private Func<Func<TimeSpan>> StartClock { get; } = ProjectBackups.StartStopwatch;
    internal ApiTryExecuteOperation(ApiTryPlanOperation planner, IApiTryDispatcher dispatch, ITransactionScopeFactory transactions, Func<Func<TimeSpan>> startClock)
        : this(planner, dispatch, transactions) => StartClock = startClock;

    public override string Name => OperationNames.ApiTryExecute;
    protected override ApiTryData Execute(ApiTryArgs args)
    {
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
        bool budgetExceeded = false;
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
                    Dictionary<string, string> after = ApiTryFiles.Observe(args, dispatch.Inspect(args.Project));
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
            && ex.Error.Reason is not (ErrorReason.EplanBusy or ErrorReason.OutcomeUnknown)
            && (ex.Error.Reason != ErrorReason.Timeout || budgetExceeded))
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
            Func<TimeSpan> elapsed = StartClock();
            object? result = dispatch.Invoke(args);
            // The member has returned. This local budget failure can be known after a successful rollback;
            // an exception raised inside the invocation still has the conservative unknown outcome.
            if (elapsed().TotalSeconds > 30)
            {
                budgetExceeded = true;
                throw new BridgeErrorException(ErrorReason.Timeout, "The API member exceeded 30 seconds; the trial was rolled back. Reduce the scratch project before retrying.");
            }
            string? value = result as string;
            if (setter && value != Convert.ToString(((Newtonsoft.Json.Linq.JValue)ApiTryPlanOperation.Parameters(args.Parameters)["value"]!).Value, System.Globalization.CultureInfo.InvariantCulture))
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The property did not read back as requested; the trial was rolled back."); }
            OperationContext.Current.ThrowIfCancellationRequested();
            var data = new ApiTryData { Value = value, Locator = result as DrawingLocator, Handle = result as ApiObjectHandle, HandleSource = result is ApiObjectHandle ? args.Group : null, Before = before, After = ApiTryFiles.Observe(args, dispatch.Inspect(args.Project)), Files = ApiTryFiles.Outputs(args) };
            if (setter)
            { data.After[key] = JsonConvert.SerializeObject(value); }
            OperationContext.Current.ThrowIfCancellationRequested();
            return data;
        }
    }
}
