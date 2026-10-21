using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Spikes.S19.Http;

/// <summary>
/// The tools of the prototype: a trivial one (s19_ping), one that reports what a handler can learn about its session
/// (s19_whoami), and a stand-in for eplan_session_select's state (s19_select, s19_selected) kept per MCP session ID.
/// </summary>
[McpServerToolType]
internal sealed class S19Tools
{
    [McpServerTool(Name = "s19_ping", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Returns 'pong'. Spike S19's trivial tool.")]
    public static string Ping() => "pong";

    [McpServerTool(Name = "s19_whoami", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Reports the MCP session ID, the negotiated protocol version, the client name as seen from the handler and from a singleton, and the remote address.")]
    public static string WhoAmI(McpServer server, RequestContext<CallToolRequestParams> request, SessionStates states, IServiceProvider services)
    {
        HttpContext? http = services.GetService<IHttpContextAccessor>()?.HttpContext;
        var result = new JsonObject
        {
            ["transport"] = http is null ? "stdio" : "http",
            ["sessionId"] = server.SessionId,
            ["negotiatedProtocolVersion"] = server.NegotiatedProtocolVersion,
            ["messageProtocolVersion"] = request.JsonRpcRequest.Context?.ProtocolVersion,
            ["clientName.handler"] = request.Server.ClientInfo?.Name,
            ["clientName.messageMeta"] = request.JsonRpcRequest.Context?.ClientInfo?.Name,
            // What WorkbenchChannel does today: a singleton asks its (root) provider for McpServer.
            ["clientName.rootProvider"] = states.RootClientName(),
            ["remoteAddress"] = http?.Connection.RemoteIpAddress?.ToString(),
            ["sessionStartedFrom"] = server.SessionId is { } id ? states.StartedFrom(id) : null,
        };
        return result.ToJsonString();
    }

    [McpServerTool(Name = "s19_select", Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Remembers an instance number for this MCP session, like eplan_session_select remembers an EPLAN process.")]
    public static string Select(McpServer server, SessionStates states, [Description("Any number.")] int instance)
    {
        states.Select(SessionStates.Key(server), instance);
        return "selected " + instance.ToString(System.Globalization.CultureInfo.InvariantCulture) + " for " + SessionStates.Key(server);
    }

    [McpServerTool(Name = "s19_selected", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Returns the instance number remembered for this MCP session, or 'none'.")]
    public static string Selected(McpServer server, SessionStates states) =>
        states.Selected(SessionStates.Key(server)) is { } instance
            ? instance.ToString(System.Globalization.CultureInfo.InvariantCulture) + " for " + SessionStates.Key(server)
            : "none for " + SessionStates.Key(server);
}

/// <summary>
/// Per-session state keyed by <see cref="ModelContextProtocol.McpSession.SessionId"/>, the way the Host could keep the
/// selected EPLAN instance per HTTP session. Stateless HTTP (and stdio) has no session ID: every caller then shares
/// the "(no session)" entry, which is what the README's per-session check shows.
/// </summary>
internal sealed class SessionStates(IServiceProvider root)
{
    private readonly ConcurrentDictionary<string, int> _selected = new();
    private readonly ConcurrentDictionary<string, string> _startedFrom = new();

    public static string Key(McpServer server) => server.SessionId ?? "(no session)";

    public void Select(string key, int instance) => _selected[key] = instance;

    public int? Selected(string key) => _selected.TryGetValue(key, out int instance) ? instance : null;

    /// <summary>The client name a singleton gets from the root provider, as WorkbenchChannel asks for it.</summary>
    public string? RootClientName() => root.GetService<McpServer>() is { } server ? server.ClientInfo?.Name ?? "(McpServer, no client info)" : "(no McpServer in the root provider)";

    public string? StartedFrom(string sessionId) => _startedFrom.TryGetValue(sessionId, out string? from) ? from : "(unknown)";

    /// <summary>ConfigureSessionOptions: logs what the starting request offers; remembers its address under the new session ID if it is already known.</summary>
    public void Started(HttpContext context)
    {
        string remote = context.Connection.RemoteIpAddress?.ToString() ?? "?";
        string minted = context.Response.Headers["Mcp-Session-Id"].ToString();
        string sent = context.Request.Headers["Mcp-Session-Id"].ToString();
        Console.Error.WriteLine($"S19 ConfigureSessionOptions: remote={remote} responseSessionId={(minted.Length > 0 ? minted : "(none yet)")} requestSessionId={(sent.Length > 0 ? sent : "(none)")}");
        if (minted.Length > 0)
        {
            _startedFrom[minted] = remote;
        }
    }

    public static string Mode(IServiceProvider services) =>
        services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value.SessionMode.ToString();
}
