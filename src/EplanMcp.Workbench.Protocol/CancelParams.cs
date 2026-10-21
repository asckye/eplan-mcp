namespace EplanMcp.Workbench.Protocol;

/// <summary>Params of <see cref="WorkbenchProtocol.CancelMethod"/>.</summary>
/// <param name="RequestId">The request ID of the question to remove.</param>
public sealed record CancelParams(string RequestId);
