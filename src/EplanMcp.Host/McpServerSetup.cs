using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host;

/// <summary>Service registration shared by <c>serve</c> and the tests: Bridge access, MCP server, tools.</summary>
internal static class McpServerSetup
{
    /// <summary>Registers everything except the transport, which the caller chooses.</summary>
    public static IMcpServerBuilder AddEplanMcp(this IServiceCollection services)
    {
        services.AddSingleton<IBridgeDiscovery>(_ => new PipeBridgeDiscovery());
        services.AddSingleton<IBridgeConnector, PipeBridgeConnector>();
        services.AddSingleton<IBridgeClient, BridgeClient>();
        services.PostConfigure<McpServerOptions>(options => ToolSchemas.Harden(options.ToolCollection ?? []));

        // Tool order in tools/list stays fixed (group, then name) so clients can cache prompts (ADR-0006).
        return services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "eplanmcp", Title = "EplanMcp", Version = HostInfo.Version };
                options.ServerInstructions = ServerInstructions.Text;
            })
            .WithTools<SessionTools>(HostJson.Options)
            .WithTools<ProjectTools>(HostJson.Options)
            .WithTools<PageTools>(HostJson.Options)
            .WithTools<DeviceTools>(HostJson.Options)
            .WithTools<ConnectionTools>(HostJson.Options)
            .WithTools<PartTools>(HostJson.Options)
            .WithTools<MessageTools>(HostJson.Options)
            .WithTools<CheckTools>(HostJson.Options)
            .WithTools<UsageTools>(HostJson.Options)
            .WithRequestFilters(filters => filters.AddCallToolFilter(UnknownArgumentFilter.Apply));
    }
}
