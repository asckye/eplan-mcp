namespace EplanMcp.Contracts;

/// <summary>
/// What a write's <c>*.plan</c> operation returns (M3 design 4.1): the changes it would make, for the dry run and the
/// approval form, and a fingerprint of the objects they touch, which <c>*.execute</c> checks again (PLAN_STALE).
/// </summary>
public sealed class WritePlan
{
    /// <summary>Every change, in the order it would be made. Empty when there is nothing to do.</summary>
    public List<PlannedChange> Changes { get; set; } = [];

    /// <summary>SHA-256 of the planned objects' state, hex; opaque to the Host.</summary>
    public string Fingerprint { get; set; } = string.Empty;
}
