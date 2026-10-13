using EplanMcp.Contracts;

namespace EplanMcp.Host.Bridge;

/// <summary>A failed Bridge call, already expressed as the error the tool returns to the model.</summary>
internal sealed class BridgeCallException : Exception
{
    public BridgeCallException()
        : this(new BridgeError())
    {
    }

    public BridgeCallException(string message)
        : this(new BridgeError { Message = message })
    {
    }

    public BridgeCallException(string message, Exception innerException)
        : base(message, innerException)
    {
        Error = new BridgeError { Message = message };
    }

    public BridgeCallException(BridgeError error)
        : base(error.Message)
    {
        Error = error;
    }

    public BridgeError Error { get; }
}
