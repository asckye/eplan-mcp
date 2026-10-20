namespace EplanMcp.Contracts;

/// <summary>
/// Whether a write changed the project (ADR-0010, mcp-tool-design 6.2). Results and errors of Write, Destructive and
/// Code tools carry one of these; read tools carry none. Two invariants hold: a retryable error is
/// <see cref="NotStarted"/> or <see cref="RolledBack"/>, and the state is <see cref="Unknown"/> exactly when the
/// reason is <see cref="ErrorReason.OutcomeUnknown"/>.
/// </summary>
public static class ExecutionState
{
    /// <summary>Nothing was changed: refused before execution, a dry run, or an empty change list.</summary>
    public const string NotStarted = "not_started";

    /// <summary>The change started, failed and was rolled back; the project is as it was before the call.</summary>
    public const string RolledBack = "rolled_back";

    /// <summary>The change was committed.</summary>
    public const string Committed = "committed";

    /// <summary>Whether the project was changed cannot be told.</summary>
    public const string Unknown = "unknown";
}
