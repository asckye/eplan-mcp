using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A13/A52/A53, S40/S41/S43: LockAllObjects → UndoStep → Transaction → QuietModeStep, inside LockingStep.</summary>
internal sealed class FunctionTransactionScope : ITransactionScope
{
    private readonly UndoManager _undoManager;
    private readonly UndoStep _undoStep;
    private readonly Transaction _transaction;
    private QuietModeStep? _quietMode;
    private bool _disposed;

    public FunctionTransactionScope(Project project, string description)
    {
        MainThread.Assert();
#pragma warning disable RS0030 // A13/A52/S41: explicit locks are required for Function.Create under Transaction.
        project.LockAllObjects();
#pragma warning restore RS0030
        _undoManager = new UndoManager();
        Transaction? transaction = null;
        try
        {
            _undoStep = _undoManager.CreateUndoStep();
            _undoStep.SetUndoDescription(description);
            transaction = new TransactionManager().CreateTransaction();
            _quietMode = new QuietModeStep(QuietModes.ShowNoDialogs);
            _transaction = transaction;
        }
        catch (Exception)
        {
            transaction?.Dispose();
            _undoManager.Dispose();
            throw;
        }
    }

    public void Commit()
    {
        EndQuietMode();
        _transaction.Commit();
    }

    public void CloseUndo() => _undoStep.CloseUndo();

    // The caller releases every new object's property list first; never CloseUndo on failure (A55/S41).
    public void Rollback()
    {
        EndQuietMode();
        _transaction.Abort();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            EndQuietMode();
            _transaction.Dispose();
        }
        finally
        {
            _undoManager.Dispose();
        }
    }

    private void EndQuietMode()
    {
        _quietMode?.Dispose();
        _quietMode = null;
    }
}
