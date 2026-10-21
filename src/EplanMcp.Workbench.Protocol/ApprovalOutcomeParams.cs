namespace EplanMcp.Workbench.Protocol;

/// <summary>Params of <see cref="WorkbenchProtocol.ApprovalOutcomeMethod"/>: what became of an approved write.</summary>
/// <param name="RequestId">The request ID of the question that was answered.</param>
/// <param name="Execution">Whether the write changed the project.</param>
/// <param name="Reason">The error code when the write failed, such as <c>PLAN_STALE</c>; else null.</param>
public sealed record ApprovalOutcomeParams(string RequestId, WriteExecution Execution, string? Reason);
