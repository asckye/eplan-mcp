using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Server;

/// <summary>Facts the Bridge reports in <c>handshake</c>; the licence callback refreshes the start-up information.</summary>
public sealed class BridgeServerOptions
{
    public string BridgeVersion { get; set; } = "0.0.0";

    public EplanInstanceInfo Eplan { get; set; } = new();

    /// <summary>Checks without holding a licence, through the main-thread dispatcher on every handshake; null means unknown.</summary>
    public Func<bool?>? ApiExtensionLicence { get; set; }

    /// <summary>The add-in's target framework (<c>TargetFrameworkAttribute</c>); null when unknown.</summary>
    public string? BuildTarget { get; set; }

    /// <summary>Where EPLAN registered the add-in (<see cref="HandshakeResult.AddinPath"/>); null when unknown.</summary>
    public string? AddinPath { get; set; }

    /// <summary>How many Hosts may be connected at the same time (design 4.2).</summary>
    public int MaxConnections { get; set; } = 4;

    /// <summary>The shortest gap between two progress notifications of one invoke; MCP asks senders to limit the rate.</summary>
    public TimeSpan ProgressInterval { get; set; } = TimeSpan.FromSeconds(1);
}
