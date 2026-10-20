using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // serve registers the locations it loaded the configuration from; tests get the environment's.
        services.TryAddSingleton(_ => HostDataLocations.FromEnvironment());
        services.TryAddSingleton(provider => SuspensionStore.For(provider.GetRequiredService<HostDataLocations>()));
        services.TryAddSingleton(TimeProvider.System);

        // serve registers the configuration it loaded; tests get the defaults, never a developer's own file.
        services.TryAddSingleton(provider => HostConfig.Defaults(provider.GetRequiredService<HostDataLocations>()));
        services.TryAddSingleton(provider => new AuditLog(provider.GetRequiredService<HostDataLocations>().Audit, provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<DanglingWrites>();

        // Approval channels are registered in the order they are tried (ADR-0005 section 3); with none, approvals are refused.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IApprovalChannel, EplanDialogChannel>());
        services.TryAddSingleton<ApprovalService>();
        services.TryAddSingleton<WritePipeline>();
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
            .WithTools<PageWriteTools>(HostJson.Options)
            .WithTools<DeviceTools>(HostJson.Options)
            .WithTools<DeviceWriteTools>(HostJson.Options)
            .WithTools<ConnectionTools>(HostJson.Options)
            .WithTools<ConnectionWriteTools>(HostJson.Options)
            .WithTools<PartTools>(HostJson.Options)
            .WithTools<MessageTools>(HostJson.Options)
            .WithTools<CheckTools>(HostJson.Options)
            .WithTools<MacroTools>(HostJson.Options)
            .WithTools<MacroWriteTools>(HostJson.Options)
            .WithTools<UsageTools>(HostJson.Options)
            .WithRequestFilters(filters => filters.AddCallToolFilter(UnknownArgumentFilter.Apply));
    }
}
