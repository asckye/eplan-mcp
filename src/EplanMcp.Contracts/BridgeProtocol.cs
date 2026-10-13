namespace EplanMcp.Contracts;

/// <summary>
/// Constants of the Host–Bridge protocol (docs/design/2026-10-13-m1-skeleton.md, section 4.2): pipe naming,
/// protocol version, JSON-RPC method names and error codes.
/// </summary>
public static class BridgeProtocol
{
    /// <summary>The protocol version this build speaks. Bumped on any incompatible change.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The oldest Bridge protocol version the Host still accepts (architecture 8.10: N and N-1).</summary>
    public const int MinimumSupportedVersion = 1;

    /// <summary>
    /// Prefix of the Bridge's named pipe; the full name is <c>EplanMcp.Bridge.&lt;pid&gt;.&lt;random hex&gt;</c>.
    /// The Host discovers instances by listing pipes with this prefix (ADR-0008).
    /// </summary>
    public const string PipePrefix = "EplanMcp.Bridge.";

    /// <summary>Largest frame either side accepts, in bytes. Larger frames are a protocol error.</summary>
    public const int MaxFrameBytes = 16 * 1024 * 1024;

    /// <summary>JSON-RPC method: version exchange; must be the first request on a connection.</summary>
    public const string HandshakeMethod = "handshake";

    /// <summary>JSON-RPC method: runs one operation, see <see cref="OperationNames"/>.</summary>
    public const string InvokeMethod = "invoke";

    /// <summary>JSON-RPC error code carrying a business error; <c>error.data</c> is a <see cref="BridgeError"/>.</summary>
    public const int BusinessErrorCode = -32000;

    /// <summary>JSON-RPC error code for an <c>invoke</c> sent before a successful <c>handshake</c>.</summary>
    public const int NotHandshakenErrorCode = -32002;
}
