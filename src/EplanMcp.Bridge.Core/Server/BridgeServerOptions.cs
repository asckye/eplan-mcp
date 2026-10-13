using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Server;

/// <summary>Static facts the Bridge reports in <c>handshake</c>; captured once on the main thread at start-up.</summary>
public sealed class BridgeServerOptions
{
    public string BridgeVersion { get; set; } = "0.0.0";

    public EplanInstanceInfo Eplan { get; set; } = new();

    /// <summary>How many Hosts may be connected at the same time (design 4.2).</summary>
    public int MaxConnections { get; set; } = 4;
}
