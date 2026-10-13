using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// What <see cref="IBridgeClient.GetStatusAsync"/> found: every discovered instance, and either the handshake with
/// the single usable one or the problem that prevents using one.
/// </summary>
internal sealed record BridgeStatus(IReadOnlyList<BridgeEndpoint> Instances, HandshakeResult? Handshake, BridgeError? Problem);
