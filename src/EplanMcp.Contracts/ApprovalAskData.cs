namespace EplanMcp.Contracts;

/// <summary>
/// What the approval dialog answered (<see cref="OperationNames.ApprovalAsk"/>), with the binding of the question it
/// answers, so that the Host can refuse an answer to another question.
/// </summary>
public sealed class ApprovalAskData
{
    /// <summary>The request ID of the question.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>The plan fingerprint of the question.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>The arguments hash of the question.</summary>
    public string ArgumentsHash { get; set; } = string.Empty;

    /// <summary>One of <see cref="ApprovalDecisions"/>.</summary>
    public string Decision { get; set; } = ApprovalDecisions.Cancelled;
}
