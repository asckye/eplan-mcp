using System.Globalization;
using System.Net;
using EplanMcp.Spikes.S19.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol.AspNetCore;

// Spike S19 (ADR-0018). "S19.Http [http]" serves MCP over Streamable HTTP on 127.0.0.1 only; "S19.Http stdio" serves
// the same tools over stdio. Settings come from environment variables so that the key never appears on a command line:
//   S19_KEY            the shared key (http only, required): 256-bit random number, base64url, 43 characters
//   S19_PORT           TCP port on 127.0.0.1 (default 0: any free port, printed in the ready line)
//   S19_ORIGINS        Origin allow-list, comma-separated (default empty: every request with an Origin header gets 403)
//   S19_SESSION_MODE   Stateless | Stateful | StatefulForInitializeClients (default: the SDK's default)
//   S19_REQUEST_LOG    JSON Lines file: one record per HTTP request, Authorization redacted
//   S19_READY_FILE     written with the endpoint URL once Kestrel listens
// Nothing is written to stdout except the MCP protocol in stdio mode (CLAUDE.md rule 5); everything else goes to stderr.
string mode = args.Length > 0 ? args[0] : "http";
if (mode == "stdio")
{
    HostApplicationBuilder stdio = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
    ToStandardError(stdio.Logging, stdio.Services);
    stdio.Services.AddSingleton<SessionStates>();
    stdio.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "s19-http", Version = "0.0.0" }).WithStdioServerTransport().WithTools<S19Tools>();
    await stdio.Build().RunAsync();
    return 0;
}

if (mode != "http")
{
    await Console.Error.WriteLineAsync("usage: S19.Http [http|stdio]");
    return 2;
}

string? key = Environment.GetEnvironmentVariable("S19_KEY");
if (key is not { Length: >= 43 })
{
    await Console.Error.WriteLineAsync("S19_KEY must hold a 256-bit key (base64url, 43 characters).");
    return 2;
}

int port = int.Parse(Environment.GetEnvironmentVariable("S19_PORT") ?? "0", CultureInfo.InvariantCulture);
string[] origins = (Environment.GetEnvironmentVariable("S19_ORIGINS") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
string? sessionMode = Environment.GetEnvironmentVariable("S19_SESSION_MODE");

// The content root is the exe's folder and configuration is not reloaded, as in TIA's HttpHostOptions.
WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = ["--hostBuilder:reloadConfigOnChange=false"],
    ContentRootPath = AppContext.BaseDirectory,
});
ToStandardError(builder.Logging, builder.Services);

// Safety (spike rule): loopback only. An explicit Listen also overrides ASPNETCORE_URLS and friends.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Listen(IPAddress.Loopback, port);
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SessionStates>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(provider => new Gate(
    key,
    origins,
    provider.GetRequiredService<TimeProvider>(),
    provider.GetRequiredService<ILogger<Gate>>(),
    RequestLog.Open(Environment.GetEnvironmentVariable("S19_REQUEST_LOG"))));

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "s19-http", Version = "0.0.0" })
    .WithHttpTransport(http =>
    {
        if (sessionMode is { Length: > 0 })
        {
            http.SessionMode = Enum.Parse<HttpServerSessionMode>(sessionMode);
        }

        // Called once per session (stateful) or once per request (stateless) with the HTTP request that started it.
        http.ConfigureSessionOptions = (context, options, _) =>
        {
            context.RequestServices.GetRequiredService<SessionStates>().Started(context);
            return Task.CompletedTask;
        };
    })
    .WithTools<S19Tools>();

WebApplication app = builder.Build();
Gate gate = app.Services.GetRequiredService<Gate>();
app.Use(gate.InvokeAsync); // first: every request, every path, before routing (no unauthenticated endpoint)
app.MapMcp("/mcp");

app.Lifetime.ApplicationStarted.Register(() =>
{
    string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    string endpoint = address + "/mcp";
    Console.Error.WriteLine("S19 listening " + endpoint + " sessionMode=" + SessionStates.Mode(app.Services));
    if (Environment.GetEnvironmentVariable("S19_READY_FILE") is { Length: > 0 } ready)
    {
        File.WriteAllText(ready, endpoint);
    }
});

await app.RunAsync();
return 0;

static void ToStandardError(ILoggingBuilder logging, IServiceCollection services)
{
    logging.ClearProviders();
    logging.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.ColorBehavior = LoggerColorBehavior.Disabled;
    });
    logging.SetMinimumLevel(LogLevel.Warning);
    logging.AddFilter("EplanMcp.Spikes", LogLevel.Information);
    services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
}
