namespace EplanMcp.Workbench.Approvals;

/// <summary>Whether the Workbench takes approval questions now (docs/design/2026-10-20-m5-operability.md#workbench-implementation).</summary>
public enum ApprovalChannelState
{
    /// <summary>The pipe is served and there is room for a question.</summary>
    Accepting,

    /// <summary>50 questions wait; the Hosts ask in the EPLAN dialog meanwhile (ADR-0014 decision 3).</summary>
    Full,

    /// <summary>Another program holds the pipe name; this Workbench serves nothing (ADR-0014 decision 2).</summary>
    NameTaken,
}
