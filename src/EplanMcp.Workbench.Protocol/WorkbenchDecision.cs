namespace EplanMcp.Workbench.Protocol;

/// <summary>An answer to a question; only <see cref="Approved"/>, in time and for the question asked, lets a write run.</summary>
public enum WorkbenchDecision
{
    /// <summary>The engineer approved.</summary>
    Approved,

    /// <summary>The engineer rejected.</summary>
    Rejected,

    /// <summary>The question was removed without a choice, for example when the Workbench quit.</summary>
    Closed,

    /// <summary>The deadline passed first.</summary>
    Expired,
}
