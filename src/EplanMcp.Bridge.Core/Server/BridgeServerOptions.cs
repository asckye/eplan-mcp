using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Server;

/// <summary>Static facts the Bridge reports in <c>handshake</c>; captured once on the main thread at start-up.</summary>
public sealed class BridgeServerOptions
{
    public string BridgeVersion { get; set; } = "0.0.0";

    public EplanInstanceInfo Eplan { get; set; } = new();

    /// <summary>The add-in's target framework (<c>TargetFrameworkAttribute</c>); null when unknown.</summary>
    public string? BuildTarget { get; set; }

    /// <summary>How many Hosts may be connected at the same time (design 4.2).</summary>
    public int MaxConnections { get; set; } = 4;

    /// <summary>The shortest gap between two progress notifications of one invoke; MCP asks senders to limit the rate.</summary>
    public TimeSpan ProgressInterval { get; set; } = TimeSpan.FromSeconds(1);
}
