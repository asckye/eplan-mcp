using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace EplanMcp.Host;

/// <summary>
/// <c>eplanmcp [serve]</c> runs the stdio MCP server; <c>eplanmcp --version</c> prints the version.
/// In serve mode stdout belongs to the MCP protocol: logs go to stderr only (CLAUDE.md rule 5).
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        switch (args)
        {
            case [] or ["serve"]:
                await ServeAsync().ConfigureAwait(false);
                return 0;
            case ["--version"]:
                Console.WriteLine(HostInfo.Version);
                return 0;
            default:
                Console.Error.WriteLine("usage: eplanmcp [serve] | --version");
                return 2;
        }
    }

    private static async Task ServeAsync()
    {
        // Content root beside the executable, not the client's working directory; no command-line configuration.
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.ColorBehavior = LoggerColorBehavior.Disabled;
        });
        builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddEplanMcp().WithStdioServerTransport();

        using IHost host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
    }
}
