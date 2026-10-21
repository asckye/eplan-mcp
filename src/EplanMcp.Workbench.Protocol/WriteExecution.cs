namespace EplanMcp.Workbench.Protocol;

/// <summary>Whether a write changed the project (ADR-0010); on the wire as the tools' <c>execution</c>, such as <c>not_started</c>.</summary>
public enum WriteExecution
{
    /// <summary>Nothing was changed.</summary>
    NotStarted,

    /// <summary>The change started, failed and was rolled back.</summary>
    RolledBack,

    /// <summary>The change was committed.</summary>
    Committed,

    /// <summary>Whether the project was changed cannot be told.</summary>
    Unknown,
}
