using System.Text.Json.Serialization;

namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// Result of <see cref="WorkbenchProtocol.ApprovalAskMethod"/>: the decision and the binding of the question it answers,
/// so that the Host can refuse an answer to another question.
/// </summary>
/// <param name="RequestId">The question's request ID.</param>
/// <param name="Fingerprint">The question's plan fingerprint.</param>
/// <param name="ArgumentsHash">The question's arguments hash.</param>
/// <param name="Decision">What the engineer decided, or why nobody did.</param>
/// <param name="InitiatedBy">
/// Protocol 2: <see cref="WorkbenchProtocol.InitiatedByWorkbench"/> when the engineer started this write from the
/// Workbench and the Workbench answered for that click (ADR-0065 decision 3); omitted otherwise. Only recorded in the
/// audit; the Host decides as for any other answer.
/// </param>
public sealed record ApprovalAskResult(
    string RequestId,
    string Fingerprint,
    string ArgumentsHash,
    WorkbenchDecision Decision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InitiatedBy = null);
