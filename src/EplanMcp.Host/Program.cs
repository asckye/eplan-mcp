using System.Globalization;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Doctor;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace EplanMcp.Host;

/// <summary>
/// <c>eplanmcp [serve]</c> runs the stdio MCP server; <c>eplanmcp doctor</c> checks the machine;
/// <c>eplanmcp audit verify</c> checks the audit hash chain;
/// <c>eplanmcp --version</c> prints the version.
/// In serve mode stdout belongs to the MCP protocol: logs go to stderr only (CLAUDE.md rule 5). Exit codes: 2 for a
/// wrong command line, 3 when serve refuses an invalid config.json.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        switch (args)
        {
            case [] or ["serve"]:
                return await ServeAsync().ConfigureAwait(false);
            case ["doctor"]:
                return await DoctorCommand.RunAsync(Console.Out, CultureInfo.CurrentUICulture, CancellationToken.None).ConfigureAwait(false);
            case ["audit", "verify"]:
                return AuditCommand.Verify(Console.Out, CultureInfo.CurrentUICulture, HostDataLocations.FromEnvironment());
            case ["--version"]:
                Console.WriteLine(HostInfo.Version);
                return 0;
            default:
                Console.Error.WriteLine("usage: eplanmcp [serve] | doctor | audit verify | --version");
                return 2;
        }
    }

    private static async Task<int> ServeAsync()
    {
        // An invalid safety configuration must not fall back to defaults silently (architecture 8.9).
        HostDataLocations locations = HostDataLocations.FromEnvironment();
        (HostConfig? config, string? problem) = HostConfig.Load(locations);
        if (config is null)
        {
            await Console.Error.WriteLineAsync($"eplanmcp: {locations.ConfigFile} {problem}. Fix or remove it; 'eplanmcp doctor' checks it.").ConfigureAwait(false);
            return 3;
        }

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
        builder.Services.AddSingleton(locations).AddSingleton(config);
        builder.Services.AddEplanMcp().WithStdioServerTransport();

        using IHost host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }
}
