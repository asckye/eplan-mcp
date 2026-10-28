namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// One open write transaction, implemented by the EPLAN adapter's <c>TransactionScope</c>: inside the dispatcher's
/// LockingStep, a SafetyPoint, the UndoStep created inside it and a QuietModeStep (coding standards 9.3, api-notes
/// A7, A9). Only <see cref="TransactionRunner"/> drives it, in a fixed order: <see cref="Commit"/> then
/// <see cref="CloseUndo"/> after a change that passed its read-back check; otherwise <see cref="CloseUndo"/> then
/// <see cref="Rollback"/>; <see cref="IDisposable.Dispose"/> in both cases. Function creation instead uses Transaction
/// (A13/A52/A53), with Abort and no CloseUndo on failure; its runner supplies shouldCommit (S41). Main thread only.
/// </summary>
public interface ITransactionScope : IDisposable
{
    /// <summary>Makes the change permanent (SafetyPoint.Commit).</summary>
    public void Commit();

    /// <summary>Closes the undo step, so that one Ctrl+Z in EPLAN undoes the whole write.</summary>
    public void CloseUndo();

    /// <summary>Takes the change back (SafetyPoint.Rollback).</summary>
    public void Rollback();
}
