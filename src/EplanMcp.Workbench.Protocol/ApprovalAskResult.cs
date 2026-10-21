namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// Result of <see cref="WorkbenchProtocol.ApprovalAskMethod"/>: the decision and the binding of the question it answers,
/// so that the Host can refuse an answer to another question.
/// </summary>
/// <param name="RequestId">The question's request ID.</param>
/// <param name="Fingerprint">The question's plan fingerprint.</param>
/// <param name="ArgumentsHash">The question's arguments hash.</param>
/// <param name="Decision">What the engineer decided, or why nobody did.</param>
public sealed record ApprovalAskResult(string RequestId, string Fingerprint, string ArgumentsHash, WorkbenchDecision Decision);
