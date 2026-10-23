using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Write entry with ordinary outcome recovery and TransactionRunner; never adds pipeline hooks.</summary>
public sealed class ActionRunExecuteOperation(int major, IProjectCatalog catalog, IActionRunner runner, ISystemMessageReader messages, ITransactionScopeFactory transactions)
    : Operation<ActionRunArgs, ActionRunData>, IWriteOperation
{
    public override string Name => OperationNames.ActionRunExecute;
    protected override ActionRunData Execute(ActionRunArgs args)
    {
        ActionRunRequest request;
        try
        {
            request = ActionRunRequest.Check(args, major, catalog, runner, "write");
            if (string.IsNullOrEmpty(args.Fingerprint)) { throw InvalidArgument("'fingerprint' from action.plan is required."); }
            if (request.StateFingerprint() != args.Fingerprint) { throw new BridgeErrorException(ErrorReason.PlanStale, "The action plan changed; request a new plan."); }
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        Committed<ActionRunData> committed = TransactionRunner.Run(transactions, "eplan_action_run", () => ActionRunOperation.Run(request, runner, messages));
        committed.Result.Warning += " " + committed.Warning;
        return committed.Result;
    }
}
