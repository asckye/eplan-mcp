using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Spikes.S4;

/// <summary>
/// Spike S4 entry point.
/// <list type="bullet">
/// <item><c>serve [--log-dir DIR]</c>: stdio MCP server for a real client (Claude Code). Every raw JSON-RPC line in
/// both directions is appended to <c>wire-*.jsonl</c> so the negotiated revision and the exact elicitation
/// messages can be read afterwards.</item>
/// </list>
/// Logs go to stderr or files only: in serve mode stdout belongs to the protocol.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "serve";
        string logDir = OptionValue(args, "--log-dir") ?? DefaultLogDir();
        Directory.CreateDirectory(logDir);
        return mode switch
        {
            "serve" => await ServeAsync(logDir).ConfigureAwait(false),
            _ => Usage(),
        };
    }

    private static async Task<int> ServeAsync(string logDir)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        using var wire = new WireLog(Path.Combine(logDir, $"wire-{stamp}-{Environment.ProcessId}.jsonl"));
        using Stream stdin = Console.OpenStandardInput();
        using Stream stdout = Console.OpenStandardOutput();
        using var input = new TeeStream(stdin, wire, "in");
        using var output = new TeeStream(stdout, wire, "out");

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services
            .AddMcpServer(options => options.ServerInfo = new Implementation { Name = "eplanmcp-spike-s4", Version = "0.0.1" })
            .WithStreamServerTransport(input, output)
            .WithTools<S4Tools>();
        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static string? OptionValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// <c>&lt;repo&gt;/.local/spikes/s4</c> (git-ignored), found by walking up to <c>global.json</c>; not
    /// %LOCALAPPDATA%, which MSIX-packaged clients redirect (api-notes A6).
    /// </summary>
    private static string DefaultLogDir()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
            {
                return Path.Combine(dir.FullName, ".local", "spikes", "s4");
            }
        }

        return Path.Combine(Path.GetTempPath(), "eplanmcp-spike-s4");
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: EplanMcp.Spikes.S4 serve [--log-dir DIR]");
        return 2;
    }
}
