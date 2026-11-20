using System.Net;
using Microsoft.Extensions.Primitives;

namespace EplanMcp.Host.Http;

/// <summary>Every HTTP path passes this gate: Host header (DNS rebinding) and Origin validation. Network access is gated by the host-only IPv4 Windows Firewall rule.</summary>
internal sealed class HttpGate(IEnumerable<string> origins)
{
    private readonly HashSet<string> _origins = new(origins, StringComparer.Ordinal);

    /// <summary>
    /// The Host header must name the bound address and port (MCP server standard PRO-18): a page that rebinds its own
    /// name to this address cannot reach the server, because the browser sends that name. Port 80 may be left out.
    /// </summary>
    public static bool HostAllowed(StringValues host, IPAddress address, int port) =>
        host.Count == 1 && (string.Equals(host[0], $"{address}:{port}", StringComparison.Ordinal) || (port == 80 && string.Equals(host[0], address.ToString(), StringComparison.Ordinal)));

    public bool OriginAllowed(StringValues origin) => origin.Count == 0 || (origin.Count == 1 && _origins.Contains(origin[0] ?? string.Empty));

    public int Check(StringValues origin) => OriginAllowed(origin) ? 0 : 403;
}
