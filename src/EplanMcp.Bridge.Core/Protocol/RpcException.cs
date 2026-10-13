namespace EplanMcp.Bridge.Core.Protocol;

/// <summary>A JSON-RPC protocol-level error answered with <see cref="Code"/>; the connection stays open.</summary>
public sealed class RpcException : Exception
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    public RpcException()
    {
    }

    public RpcException(string message)
        : base(message)
    {
        Code = InternalError;
    }

    public RpcException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = InternalError;
    }

    public RpcException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; } = InternalError;
}
