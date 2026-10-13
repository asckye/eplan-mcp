namespace EplanMcp.Contracts;

/// <summary>Result of <c>handshake</c>: what the Bridge speaks and which EPLAN it runs in.</summary>
public sealed class HandshakeResult
{
    /// <summary>The protocol version the Bridge speaks. The Host rejects it when outside its supported range.</summary>
    public int ProtocolVersion { get; set; }

    /// <summary>Bridge build version. A mismatch with the installed add-in means EPLAN still runs the old one (api-notes A8).</summary>
    public string BridgeVersion { get; set; } = string.Empty;

    /// <summary>The EPLAN instance hosting the Bridge.</summary>
    public EplanInstanceInfo Eplan { get; set; } = new();
}
