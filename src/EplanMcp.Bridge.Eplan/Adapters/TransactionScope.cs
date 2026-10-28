using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The SafetyPoint scope for writes; Function.Create uses FunctionTransactionScope (A13/A52/A53). Rule 7 applies here;
/// exports use quiet mode alone in EplanExporter, changing no design data. Inside the
/// dispatcher's LockingStep: the SafetyPoint first, then the UndoStep inside it, described for EPLAN's undo list, then
/// QuietModeStep(ShowNoDialogs). The UndoStep must never be created outside the SafetyPoint, which silently stops the
/// rollback (api-notes A7, spike S2). The order of the calls is <see cref="TransactionRunner"/>'s; this class follows
/// spike S10's verified shape in the dispatcher (A14): quiet mode ends before the commit or the rollback, the undo
/// manager is released before the SafetyPoint. Main thread only; never kept beyond one write.
/// </summary>
internal sealed class TransactionScope : ITransactionScope
{
    private readonly SafetyPoint _safetyPoint;
    private readonly UndoManager _undoManager;
    private readonly UndoStep _undoStep;
    private QuietModeStep? _quietMode;
    private bool _disposed;

    public TransactionScope(string undoDescription)
    {
        MainThread.Assert();
        SafetyPoint safetyPoint = SafetyPoint.Create();
        UndoManager? undoManager = null;
        try
        {
            undoManager = new UndoManager();
            UndoStep undoStep = undoManager.CreateUndoStep();
            undoStep.SetUndoDescription(undoDescription);
            _quietMode = new QuietModeStep(QuietModes.ShowNoDialogs);
            _safetyPoint = safetyPoint;
            _undoManager = undoManager;
            _undoStep = undoStep;
        }
        catch (Exception)
        {
            // Nothing was changed yet; a SafetyPoint released without a commit rolls back (A7).
            undoManager?.Dispose();
            safetyPoint.Dispose();
            throw;
        }
    }

    public void Commit()
    {
        EndQuietMode();
        _safetyPoint.Commit();
    }

    public void CloseUndo() => _undoStep.CloseUndo();

    public void Rollback()
    {
        EndQuietMode();
        _safetyPoint.Rollback();
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
            _undoManager.Dispose();
        }
        finally
        {
            _safetyPoint.Dispose();
        }
    }

    private void EndQuietMode()
    {
        _quietMode?.Dispose();
        _quietMode = null;
    }
}
