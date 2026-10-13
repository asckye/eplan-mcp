using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core;

/// <summary>
/// A business error raised inside the Bridge. It crosses the pipe as a JSON-RPC error whose <c>data</c> is
/// <see cref="Error"/> (design 4.2); the Host turns it into a tool error.
/// </summary>
public sealed class BridgeErrorException : Exception
{
    public BridgeErrorException()
        : this(ErrorReason.Internal, "Internal error.")
    {
    }

    public BridgeErrorException(string message)
        : this(ErrorReason.Internal, message)
    {
    }

    public BridgeErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
        Error = new BridgeError { Reason = ErrorReason.Internal, Message = message };
    }

    public BridgeErrorException(string reason, string message, bool retryable = false, Dictionary<string, string>? metadata = null)
        : base(message)
    {
        Error = new BridgeError { Reason = reason, Message = message, Retryable = retryable, Metadata = metadata };
    }

    public BridgeError Error { get; }
}
