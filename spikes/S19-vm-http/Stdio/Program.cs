using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol.Server;

// Spike S19 baseline: the Host's way of starting (Program.ServeAsync) with one trivial tool, without ASP.NET Core.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.ColorBehavior = LoggerColorBehavior.Disabled;
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "s19-stdio", Version = "0.0.0" }).WithStdioServerTransport().WithTools<PingTool>();
await builder.Build().RunAsync();

[McpServerToolType]
internal sealed class PingTool
{
    [McpServerTool(Name = "s19_ping", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Returns 'pong'. Spike S19's trivial tool.")]
    public static string Ping() => "pong";
}
