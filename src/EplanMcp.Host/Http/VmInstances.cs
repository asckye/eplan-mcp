using System.Text.Json;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Http;

internal static class VmInstances
{
    // Discovery only: unlike doctor, this never connects to a Bridge or calls EPLAN.
    public static async Task<int> WriteAsync(IBridgeDiscovery discovery, TextWriter output)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(new { SchemaVersion = 1, Processes = discovery.Discover().Select(e => e.ProcessId).Distinct().Order().ToArray() }, HostJson.Options)).ConfigureAwait(false);
        return 0;
    }
}

internal sealed class HttpStartException(string message) : InvalidOperationException(message);
