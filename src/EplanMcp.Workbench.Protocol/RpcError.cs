namespace EplanMcp.Workbench.Protocol;

/// <summary>The error of a JSON-RPC response; codes are JSON-RPC's or <see cref="WorkbenchProtocol"/>'s.</summary>
public sealed record RpcError(int Code, string Message);
