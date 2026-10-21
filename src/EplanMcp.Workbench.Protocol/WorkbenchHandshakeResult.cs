namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// Result of <see cref="WorkbenchProtocol.HandshakeMethod"/>. A Workbench that speaks no version in the Host's range
/// answers with <see cref="WorkbenchProtocol.UnsupportedVersionErrorCode"/> instead.
/// </summary>
/// <param name="ProtocolVersion">The version both sides speak on this connection, within the Host's range.</param>
/// <param name="WorkbenchVersion">The Workbench's program version.</param>
/// <param name="Accepting">
/// Whether the Workbench takes a new question now: false, for example, while 50 questions wait (ADR-0014). The Host then
/// asks elsewhere and sends nothing more.
/// </param>
public sealed record WorkbenchHandshakeResult(int ProtocolVersion, string WorkbenchVersion, bool Accepting);
