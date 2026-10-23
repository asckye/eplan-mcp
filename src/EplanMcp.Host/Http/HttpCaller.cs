using EplanMcp.Host.Safety;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Http;

/// <summary>The accessor owns request-local state; no caller information survives into another request.</summary>
internal sealed class HttpCaller(IHttpContextAccessor accessor)
{
    public string? Address => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? Name => Address is { } address ? $"{accessor.HttpContext?.Items["mcpClient"] ?? "Not provided"} (via network: {address})" : null;

    public McpRequestHandler<CallToolRequestParams, CallToolResult> Capture(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (context, cancellationToken) =>
        {
            if (accessor.HttpContext is { } http)
            {
                http.Items["mcpClient"] = ApprovalTexts.Value(context.JsonRpcRequest.Context?.ClientInfo?.Name ?? context.Server.ClientInfo?.Name ?? "Not provided", zh: false);
            }

            return next(context, cancellationToken);
        };
}
