namespace EplanMcp.Contracts;

/// <summary>
/// A business error from the Bridge, carried as <c>error.data</c> of a JSON-RPC error with code
/// <see cref="BridgeProtocol.BusinessErrorCode"/>. Mirrors the tool error shape (mcp-tool-design 6.2).
/// </summary>
public sealed class BridgeError
{
    /// <summary>One of <see cref="ErrorReason"/>.</summary>
    public string Reason { get; set; } = ErrorReason.Internal;

    /// <summary>Tells the model what to do next. No stack traces, local paths or internal type names.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Whether the same call may succeed when retried unchanged.</summary>
    public bool Retryable { get; set; }

    /// <summary>Optional details, for example the object that was not found.</summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
