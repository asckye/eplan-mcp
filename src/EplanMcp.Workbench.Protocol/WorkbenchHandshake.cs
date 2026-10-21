namespace EplanMcp.Workbench.Protocol;

/// <summary>Params of <see cref="WorkbenchProtocol.HandshakeMethod"/>, the Host's first request on a connection.</summary>
/// <param name="ProtocolVersion">The newest protocol version the Host speaks.</param>
/// <param name="MinProtocolVersion">The oldest protocol version the Host speaks.</param>
/// <param name="HostVersion">The Host's program version, such as <c>0.1.0</c>.</param>
/// <param name="HostProcessId">The Host's process ID; the Workbench checks it against the pipe's client.</param>
public sealed record WorkbenchHandshake(int ProtocolVersion, int MinProtocolVersion, string HostVersion, int HostProcessId);
