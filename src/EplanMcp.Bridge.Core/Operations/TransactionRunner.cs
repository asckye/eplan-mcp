using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Runs a write's change in one transaction and says what became of it (M3 design 4.2, ADR-0010 section 2). The change
/// does its own read-back check and throws when it fails, so a commit only ever follows a passed check (coding
/// standards 9.3, rule 9). Any exception from the change rolls everything back and is rethrown as "rolled back"; a
/// commit or rollback that itself fails makes the outcome unknown (OUTCOME_UNKNOWN), never "rolled back" by guess;
/// a problem after the commit is a warning on the committed result. The order of the calls follows what spikes S1,
/// S2 and S10 verified (api-notes A7, A9, A14). Main thread only.
/// </summary>
public static class TransactionRunner
{
    /// <summary>The prefix of the undo step EPLAN lists for every write (M3 design 4.6).</summary>
    public const string UndoPrefix = "AI: ";

    public static Committed<T> Run<T>(ITransactionScopeFactory transactions, string tool, Func<T> change)
    {
        ITransactionScope scope = transactions.Begin(UndoPrefix + tool);
        T result;
        try
        {
            result = change();
        }
        catch (Exception failure)
        {
            Abandon(scope, tool);
            if (failure is BridgeErrorException known)
            {
                known.Error.Execution = ExecutionState.RolledBack;
            }

            throw;
        }

        try
        {
            scope.Commit();
        }
        catch (Exception ex)
        {
            DisposeAfterFailure(scope);
            throw Unknown(tool, "committing", ex);
        }

        string? warning = null;
        try
        {
            scope.CloseUndo();
        }
        catch (Exception)
        {
            // The change is in the project; only its undo step may be broken (ADR-0010 section 1: a warning).
            warning = $"The change was committed, but EPLAN did not close its undo step, so Ctrl+Z in EPLAN may not undo '{UndoPrefix}{tool}' in one step.";
        }

        try
        {
            scope.Dispose();
        }
        catch (Exception)
        {
            warning ??= "The change was committed, but EPLAN reported an error while ending the transaction. Ask the engineer to check the project in EPLAN.";
        }

        return new Committed<T>(result, warning);
    }

    /// <summary>
    /// Closes the undo step, then rolls back (S1, S2: an UndoStep inside the SafetyPoint does not stop the rollback).
    /// A failed close leaves the data alone; a failed rollback or release leaves the project in an unknown state.
    /// </summary>
    private static void Abandon(ITransactionScope scope, string tool)
    {
        try
        {
            scope.CloseUndo();
        }
        catch (Exception)
        {
            // Nothing to do: the rollback below decides what happens to the data.
        }

        try
        {
            scope.Rollback();
            scope.Dispose();
        }
        catch (Exception ex)
        {
            DisposeAfterFailure(scope);
            throw Unknown(tool, "rolling back", ex);
        }
    }

    private static void DisposeAfterFailure(ITransactionScope scope)
    {
        try
        {
            scope.Dispose();
        }
        catch (Exception)
        {
            // Already reported as an unknown outcome; a second failure adds nothing.
        }
    }

    private static BridgeErrorException Unknown(string tool, string step, Exception cause)
    {
        var error = new BridgeErrorException(
            ErrorReason.OutcomeUnknown,
            $"EPLAN failed while {step} the change, so whether the project changed is unknown. Do not call this tool again; ask the engineer to check the project in EPLAN, where Ctrl+Z undoes '{UndoPrefix}{tool}' if the change is there.",
            cause);
        error.Error.Execution = ExecutionState.Unknown;
        return error;
    }
}
