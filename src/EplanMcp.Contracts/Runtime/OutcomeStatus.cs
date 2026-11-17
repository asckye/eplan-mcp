namespace EplanMcp.Contracts;

/// <summary>
/// The states of a recorded write. The last four are final; <see cref="Queued"/> and <see cref="Running"/> mean "ask
/// again", and <see cref="Expired"/> that the record was evicted, which the Host treats as unknown.
/// </summary>
public static class OutcomeStatus
{
    /// <summary>Received, waiting for EPLAN's main thread.</summary>
    public const string Queued = "queued";

    /// <summary>Running on EPLAN's main thread.</summary>
    public const string Running = "running";

    /// <summary>Not executed. Also the answer for an ID the Bridge has not seen, which then never runs.</summary>
    public const string NotStarted = ExecutionState.NotStarted;

    /// <summary>Failed and rolled back.</summary>
    public const string RolledBack = ExecutionState.RolledBack;

    /// <summary>Committed.</summary>
    public const string Committed = ExecutionState.Committed;

    /// <summary>The rollback itself failed.</summary>
    public const string Unknown = ExecutionState.Unknown;

    /// <summary>The record was evicted from the Bridge's bounded table.</summary>
    public const string Expired = "expired";
}
