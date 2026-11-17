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
    private readonly bool _undoCommitted;
    private readonly bool _undoAbandoned;
    private bool _committed;
    private bool _transactionDisposed;

    public FunctionTransactionScope(Project project, string description, bool undoCommitted = false, bool undoAbandoned = false)
    {
        MainThread.Assert();
        _undoCommitted = undoCommitted;
        _undoAbandoned = undoAbandoned;
#pragma warning disable RS0030 // A13/A52/S41: explicit locks are required for Function.Create under Transaction.
        project.LockAllObjects();
#pragma warning restore RS0030
        _undoManager = new UndoManager();
        UndoStep? undoStep = null;
        Transaction? transaction = null;
        try
        {
            _undoStep = undoStep = _undoManager.CreateUndoStep();
            _undoStep.SetUndoDescription(description);
            transaction = new TransactionManager().CreateTransaction();
            _quietMode = new QuietModeStep(QuietModes.ShowNoDialogs);
            _transaction = transaction;
        }
        catch (Exception nativeFailure)
        {
            WriteExceptionDiagnostics.Report(nativeFailure);
            transaction?.Dispose();
            undoStep?.Dispose();
            _undoManager.Dispose();
            throw;
        }
    }

    public void Commit()
    {
        EndQuietMode();
        _transaction.Commit();
        _committed = true;
        if (_undoCommitted)
        {
            // A55: no native transaction wrapper survives into fresh committed readback.
            _transaction.Dispose();
            _transactionDisposed = true;
            _quietMode = new QuietModeStep(QuietModes.ShowNoDialogs);
        }
    }

    public void CloseUndo() => _undoStep.CloseUndo();

    // The caller releases every new object's property list first; never CloseUndo on failure (A55/S41).
    public void Rollback()
    {
        EndQuietMode();
        // Agent eval 2026-11-05, live T8b: Abort leaves interruption points and cable lines on the page; undo removes them once committed.
        if (!_committed && _undoAbandoned)
        { _transaction.Commit(); _committed = true; _transaction.Dispose(); _transactionDisposed = true; }
        // UndoStep's documented lifetime spans committed transactions; D5 keeps generation in that same step.
        if (_committed && _undoCommitted)
        { using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs); _undoStep.DoUndo(); }
        else
        { _transaction.Abort(); }
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
            if (!_transactionDisposed)
            { _transaction.Dispose(); }
        }
        finally
        {
            // UndoStep owns the native protocol too (B7 XML: use inside using). End it on this thread,
            // after Abort/transaction release, before its manager; never leave refusal cleanup to a finalizer.
            try
            { _undoStep.Dispose(); }
            finally { _undoManager.Dispose(); }
        }
    }

    private void EndQuietMode()
    {
        _quietMode?.Dispose();
        _quietMode = null;
    }
}
