namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// What became of an approved write (workbench-ui APR-12), from <c>approval.outcome</c>. It only changes what the
/// answered group shows: it decides nothing and enables nothing.
/// </summary>
public enum QuestionExecution
{
    /// <summary>Not an approved write: rejected, ended otherwise, or a release, which has no outcome.</summary>
    None,

    /// <summary>Approved; the write has not reported yet.</summary>
    Running,

    /// <summary>The write changed the project.</summary>
    Committed,

    /// <summary>The write failed and was rolled back.</summary>
    RolledBack,

    /// <summary>Nothing was changed, for example because the plan was stale.</summary>
    NotStarted,

    /// <summary>Whether the project changed cannot be told; writes are suspended (ADR-0010 section 3).</summary>
    Unknown,

    /// <summary>The connection closed without an outcome; nothing is inferred.</summary>
    NotReceived,
}
