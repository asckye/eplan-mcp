namespace EplanMcp.Contracts;

/// <summary>
/// What the Bridge recorded for a write (ADR-0010, section 2). Metadata only, never project content; the Host has the
/// plan and its change list.
/// </summary>
public sealed class OutcomeGetData
{
    /// <summary>One of <see cref="OutcomeStatus"/>.</summary>
    public string Status { get; set; } = OutcomeStatus.NotStarted;

    /// <summary>The operation recorded under the request ID; null when the Bridge had not seen the ID.</summary>
    public string? Operation { get; set; }

    /// <summary>The error's reason when the write was refused or rolled back; null otherwise.</summary>
    public string? Reason { get; set; }
}
