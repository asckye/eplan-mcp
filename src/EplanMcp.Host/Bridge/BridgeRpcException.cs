using System.Text.Json.Nodes;

namespace EplanMcp.Host.Bridge;

/// <summary>A JSON-RPC error response from the Bridge. <see cref="ErrorData"/> holds a BridgeError for business errors.</summary>
internal sealed class BridgeRpcException : Exception
{
    public BridgeRpcException()
    {
    }

    public BridgeRpcException(string message)
        : base(message)
    {
    }

    public BridgeRpcException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BridgeRpcException(int code, string message, JsonNode? data)
        : base(message)
    {
        Code = code;
        ErrorData = data;
    }

    public int Code { get; }

    /// <summary>The <c>error.data</c> member of the response.</summary>
    public JsonNode? ErrorData { get; }
}
