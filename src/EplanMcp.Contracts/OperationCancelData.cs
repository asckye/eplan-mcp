namespace EplanMcp.Contracts;

/// <summary>What <see cref="OperationNames.OperationCancel"/> found.</summary>
public sealed class OperationCancelData
{
    /// <summary>
    /// True when the invoke was queued or running and now stops at its next safe point. False when the Bridge had no
    /// such invoke: it has answered already, or has not received it yet; the cancel then has no effect.
    /// </summary>
    public bool Found { get; set; }
}
