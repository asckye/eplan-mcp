using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;

namespace EplanMcp.Host.Http;

internal static class VmHttp
{
    public static WebApplication Build(HttpArguments options, HostDataLocations locations, HostConfig config, IBridgeDiscovery discovery)
    {
        // Pin the exact pipe, not just the PID: a restarted EPLAN must never become an implicit replacement.
        BridgeEndpoint[] candidates = [.. discovery.Discover().Where(e => options.ProcessId is null || e.ProcessId == options.ProcessId)];
        if (candidates.Length != 1)
        {
            throw new HttpStartException("HTTP requires exactly one EPLAN instance; use --process <pid> when several are running.");
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear(); // Environment/appsettings must never add listeners or HTTP logging.
        Program.ConfigureLogging(builder.Logging, builder.Services);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = 1024 * 1024;
            kestrel.Limits.MaxConcurrentConnections = 32;
            kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            kestrel.Listen(options.Address, options.Port);
        });
        var accessor = new HttpContextAccessor();
        var caller = new HttpCaller(accessor);
        builder.Services.AddSingleton<IHttpContextAccessor>(accessor).AddSingleton(caller);
        builder.Services.AddSingleton(locations).AddSingleton(config).AddSingleton(new ToolProfileChoice(ToolProfile.Full));
        builder.Services.AddSingleton<IHostedService, HttpServiceStateStore>();
        builder.Services.AddEplanMcp().WithHttpTransport(http => http.SessionMode = HttpServerSessionMode.Stateless)
            .WithRequestFilters(filters => filters.AddCallToolFilter(caller.Capture));
        builder.Services.AddSingleton<IBridgeDiscovery>(new PinnedDiscovery(discovery, candidates[0]));
        // The pinned client replaces AddEplanMcp's, behind the same window watch; the container still disposes it.
        builder.Services.AddSingleton(services => new BridgeClient(services.GetRequiredService<IBridgeDiscovery>(), services.GetRequiredService<IBridgeConnector>(), services.GetRequiredService<ILogger<BridgeClient>>()) { FixedProcessId = candidates[0].ProcessId });
        WebApplication app = builder.Build();
        var gate = new HttpGate(config.HttpAllowedOrigins);
        app.Use(async (context, next) =>
        {
            int status = !HttpGate.HostAllowed(context.Request.Headers.Host, options.Address, context.Connection.LocalPort) ? 403
                : gate.Check(context.Request.Headers.Origin);
            try
            {
                await app.Services.GetRequiredService<AuditLog>().AppendAsync(new JsonObject { ["kind"] = "http_request", ["status"] = status == 0 ? "accepted" : status.ToString(System.Globalization.CultureInfo.InvariantCulture) }, context.RequestAborted).ConfigureAwait(false);
            }
            catch (AuditUnavailableException) when (status != 0)
            {
                // A storage failure must not change the generic refusal; accepted requests still fail closed.
            }
            if (status != 0)
            {
                context.Response.StatusCode = status;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
        app.MapGet("/health", () => Results.Ok(new { ok = true, service = "eplanmcp", version = HostInfo.Version }));
        app.MapMcp("/mcp");
        return app;
    }

    private sealed class PinnedDiscovery(IBridgeDiscovery discovery, BridgeEndpoint target) : IBridgeDiscovery
    {
        public IReadOnlyList<BridgeEndpoint> Discover() => [.. discovery.Discover().Where(e => e == target)];
    }
}
