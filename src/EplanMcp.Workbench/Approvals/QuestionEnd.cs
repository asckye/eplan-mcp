namespace EplanMcp.Workbench.Approvals;

/// <summary>How a question ended (docs/design/2026-10-20-m5-operability.md#workbench-implementation).</summary>
public enum QuestionEnd
{
    /// <summary>The engineer approved, or confirmed the release of a suspension.</summary>
    Approved,

    /// <summary>The engineer rejected, or kept writes suspended.</summary>
    Rejected,

    /// <summary>The deadline passed first.</summary>
    Expired,

    /// <summary>The client cancelled the call (<c>cancel</c>).</summary>
    Cancelled,

    /// <summary>The Host's connection broke, or it broke the protocol, before an answer.</summary>
    Disconnected,

    /// <summary>The Workbench quit with the question open (DLG-11).</summary>
    Closed,
}
