using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// What <see cref="IBridgeClient.GetStatusAsync"/> found: every discovered instance, either the handshake with the
/// instance in use or the problem that prevents using one, and the process chosen with eplan_session_select, if any.
/// </summary>
internal sealed record BridgeStatus(IReadOnlyList<BridgeEndpoint> Instances, HandshakeResult? Handshake, BridgeError? Problem, int? SelectedProcessId = null);
