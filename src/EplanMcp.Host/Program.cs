using System.Globalization;
using System.Text;
using EplanMcp.Host.Addin;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Doctor;
using EplanMcp.Host.Http;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace EplanMcp.Host;

/// <summary>
/// <c>eplanmcp [serve]</c> runs the stdio MCP server; <c>eplanmcp doctor</c> checks the machine;
/// <c>eplanmcp audit verify</c> checks the audit hash chain; <c>eplanmcp client-config</c> writes or removes the server in
/// AI clients' configuration; <c>eplanmcp install-addin</c> and <c>uninstall-addin</c> register and unregister the EPLAN
/// add-in; <c>eplanmcp pack …</c> scans, checks, exports and installs engineering packs (M6); with <c>--format json</c>
/// these print JSON for the Workbench; <c>eplanmcp --version</c> prints the version.
/// In serve mode stdout belongs to the MCP protocol: logs go to stderr only (CLAUDE.md rule 5). Exit codes: 2 for a
/// wrong command line, 3 when serve refuses an invalid config.json.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        switch (CliArguments.Parse(args))
        {
            case { Command: CliCommand.Serve } serve:
                return await ServeAsync(serve).ConfigureAwait(false);
            case { Command: CliCommand.Version }:
                Console.WriteLine(HostInfo.Version);
                return 0;
            case { Json: true } json:
                return await RunJsonAsync(json).ConfigureAwait(false);
            case { } text:
                return await RunAsync(text, Console.Out).ConfigureAwait(false);
            default:
                Console.Error.WriteLine(CliArguments.Usage);
                return 2;
        }
    }

    private static async Task<int> RunAsync(CliArguments cli, TextWriter output) => cli switch
    {
        { Command: CliCommand.Doctor } => await DoctorCommand.RunAsync(output, CultureInfo.CurrentUICulture, cli.Json, CancellationToken.None).ConfigureAwait(false),
        { KeyAction: { } action } => await VmKey.RunAsync(action, HostDataLocations.FromEnvironment(), output, CancellationToken.None).ConfigureAwait(false),
        { Firewall: { } firewall } => VmFirewallCommand.ForMachine().Run(firewall, output),
        { Command: CliCommand.AddinVariants } => AddinCommand.ForMachine().ListVariants(output, cli.Json),
        { ClientConfig: { } request } => ClientConfigCommand.ForMachine().Run(request, output, CultureInfo.CurrentUICulture, cli.Json),
        { Pack: { } pack } => await PackCommand.RunAsync(pack, HostDataLocations.FromEnvironment(), output, cli.Json, CancellationToken.None).ConfigureAwait(false),
        { Command: CliCommand.InstallAddin or CliCommand.UninstallAddin } => await AddinCommand.ForMachine().RunAsync(
            cli.Command == CliCommand.InstallAddin ? AddinAction.Install : AddinAction.Uninstall, cli.Variants ?? [], output, Console.Error, CultureInfo.CurrentUICulture, cli.Json).ConfigureAwait(false),
        _ => AuditCommand.Verify(output, CultureInfo.CurrentUICulture, HostDataLocations.FromEnvironment(), cli.Json),
    };

    /// <summary>The Workbench reads the JSON as UTF-8, so it bypasses Console.Out and the console's code page.</summary>
    private static async Task<int> RunJsonAsync(CliArguments cli)
    {
        using var utf8 = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return await RunAsync(cli, utf8).ConfigureAwait(false);
    }

    private static async Task<int> ServeAsync(CliArguments cli)
    {
        // An invalid safety configuration must not fall back to defaults silently (architecture 8.9).
        HostDataLocations locations = HostDataLocations.FromEnvironment();
        (HostConfig? config, string? problem) = HostConfig.Load(locations);
        if (config is null)
        {
            await Console.Error.WriteLineAsync($"eplanmcp: {locations.ConfigFile} {problem}. Fix or remove it; 'eplanmcp doctor' checks it.").ConfigureAwait(false);
            return 3;
        }

        if (cli.Http is { } http)
        {
            try
            {
                var app = VmHttp.Build(http, locations, config, new PipeBridgeDiscovery());
                await using (app.ConfigureAwait(false))
                {
                    await app.RunAsync().ConfigureAwait(false);
                }
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
            {
                await Console.Error.WriteLineAsync("HTTP service could not start. Check the IPv4/port, EPLAN selection and 'vm-key create'.").ConfigureAwait(false);
                return 3;
            }
        }

        // Content root beside the executable, not the client's working directory; no command-line configuration.
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
        ConfigureLogging(builder.Logging, builder.Services);
        builder.Services.AddSingleton(locations).AddSingleton(config).AddSingleton(new ToolProfileChoice(cli.Profile));
        builder.Services.AddEplanMcp().WithStdioServerTransport();

        using IHost host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    internal static void ConfigureLogging(ILoggingBuilder logging, IServiceCollection services)
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.ColorBehavior = LoggerColorBehavior.Disabled;
        });
        services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    }
}
