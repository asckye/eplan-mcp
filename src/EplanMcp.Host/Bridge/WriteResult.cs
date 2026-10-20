using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// A committed write. <see cref="Recovered"/>: the response was lost and <c>outcome.get</c> reported the commit, so
/// there is no <see cref="Data"/>; the caller reports the plan's changes instead (ADR-0010 section 2).
/// </summary>
internal sealed record WriteResult<TData>(TData? Data, bool Recovered);

/// <summary>ADR-0010's two invariants for the errors of writes, checked wherever the Host stamps one.</summary>
internal static class ExecutionInvariants
{
    /// <summary>
    /// A retryable error left the project as it was, and the state is unknown exactly when the reason is
    /// OUTCOME_UNKNOWN. False also when the state is missing or not one of <see cref="ExecutionState"/>.
    /// </summary>
    public static bool Hold(BridgeError error) =>
        error.Execution is ExecutionState.NotStarted or ExecutionState.RolledBack or ExecutionState.Committed or ExecutionState.Unknown
        && (!error.Retryable || error.Execution is ExecutionState.NotStarted or ExecutionState.RolledBack)
        && (error.Execution == ExecutionState.Unknown) == (error.Reason == ErrorReason.OutcomeUnknown);
}
