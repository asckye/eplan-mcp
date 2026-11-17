using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Opens a <see cref="TransactionScope"/> for each write. Main thread, inside the dispatcher's LockingStep.</summary>
internal sealed class EplanTransactions : ITransactionScopeFactory
{
    public ITransactionScope Begin(string undoDescription) => new TransactionScope(undoDescription);
}
