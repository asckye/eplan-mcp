using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Trial execution uses the action runner and ordinary transaction/outcome infrastructure.</summary>
public sealed class ActionTryExecuteOperation(IProjectCatalog catalog, IActionRunner runner, ISystemMessageReader messages, ITransactionScopeFactory transactions)
    : Operation<ActionTryArgs, ActionRunData>, IWriteOperation
{
    public override string Name => OperationNames.ActionTryExecute;
    protected override ActionRunData Execute(ActionTryArgs args)
    {
        ActionRunRequest request;
        try
        {
            request = ActionTryPlanOperation.Check(args, catalog, runner);
            if (string.IsNullOrEmpty(args.Fingerprint) || request.StateFingerprint() != args.Fingerprint)
            { throw new BridgeErrorException(ErrorReason.PlanStale, "The trial plan changed; request a new plan."); }
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Committed<ActionRunData> result = TransactionRunner.Run(transactions, "eplan_action_try", () => ActionRunOperation.Run(request, runner, messages, unverified: true));
            result.Result.Warning += " " + result.Warning;
            return result.Result;
        }
        catch (Exception ex) when (elapsed.Elapsed.TotalSeconds > 30 || ex is BridgeErrorException { Error.Reason: ErrorReason.OutcomeUnknown })
        { throw new BridgeErrorException(ErrorReason.OutcomeUnknown, "Trial timed out or showed a modal dialog; do not retry. Ask the engineer to inspect EPLAN.", ex) { Error = { Execution = ExecutionState.Unknown } }; }
    }
}
