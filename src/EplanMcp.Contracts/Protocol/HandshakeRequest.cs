namespace EplanMcp.Contracts;

/// <summary>Parameters of <c>handshake</c>, sent by the Host as the first request on a connection.</summary>
public sealed class HandshakeRequest
{
    /// <summary>Newest protocol version the Host speaks.</summary>
    public int ProtocolVersion { get; set; }

    /// <summary>Oldest protocol version the Host still accepts.</summary>
    public int MinProtocolVersion { get; set; }

    /// <summary>Host build version, for logs and for <c>eplanmcp doctor</c>.</summary>
    public string HostVersion { get; set; } = string.Empty;
}
