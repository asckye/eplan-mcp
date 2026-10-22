using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Packs;
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

        // Approval channels are registered in the order they are tried (ADR-0011), which no setting changes: the Workbench
        // while it runs, then EPLAN's dialog (ADR-0014 decision 4). With none, approvals are refused.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IApprovalChannel, WorkbenchChannel>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IApprovalChannel, EplanDialogChannel>());
        services.TryAddSingleton<ApprovalService>();
        services.TryAddSingleton<BackupFolders>();
        services.TryAddSingleton<WritePipeline>();
        services.TryAddSingleton<ExportFolders>();
        services.TryAddSingleton<ExportRunner>();
        services.TryAddSingleton<ImportFiles>();
        services.TryAddSingleton<PackLibrary>();
        services.TryAddSingleton<IReferenceProjectSteps, GapOnlyReferenceProjectSteps>();
        services.PostConfigure<McpServerOptions>(options => ToolSchemas.Harden(options.ToolCollection ?? []));
        services.TryAddSingleton(new ToolProfileChoice(ToolProfile.Full));
        services.AddOptions<McpServerOptions>().PostConfigure<ToolProfileChoice>((options, choice) =>
        {
            ToolProfiles.Apply(options.ToolCollection ?? [], choice.Profile);

            // The pack resources belong to the pack toolset, which only the full profile offers (ADR-0022).
            if (choice.Profile != ToolProfile.Full)
            {
                options.ResourceCollection?.Clear();
            }
        });

        // Tool order in tools/list stays fixed (group, then name) so clients can cache prompts (ADR-0006).
        return services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "eplanmcp", Title = "EplanMcp", Version = HostInfo.Version };
                options.ServerInstructions = ServerInstructions.Text;
            })
            .WithTools<SessionTools>(HostJson.Options)
            .WithTools<ProjectTools>(HostJson.Options)
            .WithTools<RevisionTools>(HostJson.Options)
            .WithTools<PageTools>(HostJson.Options)
            .WithTools<PageWriteTools>(HostJson.Options)
            .WithTools<PageDestructiveTools>(HostJson.Options)
            .WithTools<DeviceTools>(HostJson.Options)
            .WithTools<DeviceWriteTools>(HostJson.Options)
            .WithTools<DeviceDestructiveTools>(HostJson.Options)
            .WithTools<ConnectionTools>(HostJson.Options)
            .WithTools<ConnectionWriteTools>(HostJson.Options)
            .WithTools<ConnectionDestructiveTools>(HostJson.Options)
            .WithTools<CableTools>(HostJson.Options)
            .WithTools<CableDestructiveTools>(HostJson.Options)
            .WithTools<TerminalTools>(HostJson.Options)
            .WithTools<TerminalDestructiveTools>(HostJson.Options)
            .WithTools<PlcTools>(HostJson.Options)
            .WithTools<PartTools>(HostJson.Options)
            .WithTools<PartMasterDataTools>(HostJson.Options)
            .WithTools<MessageTools>(HostJson.Options)
            .WithTools<CheckTools>(HostJson.Options)
            .WithTools<CheckWriteTools>(HostJson.Options)
            .WithTools<ReportWriteTools>(HostJson.Options)
            .WithTools<LanguageWriteTools>(HostJson.Options)
            .WithTools<LanguageDestructiveTools>(HostJson.Options)
            .WithTools<ProjectDestructiveTools>(HostJson.Options)
            .WithTools<MacroTools>(HostJson.Options)
            .WithTools<MacroWriteTools>(HostJson.Options)
            .WithTools<PlaceholderTools>(HostJson.Options)
            .WithTools<PlaceholderWriteTools>(HostJson.Options)
            .WithTools<ExportTools>(HostJson.Options)
            .WithTools<ImageExportTools>(HostJson.Options)
            .WithTools<ProjectExportTools>(HostJson.Options)
            .WithTools<ImportTools>(HostJson.Options)
            .WithTools<MasterDataExportTools>(HostJson.Options)
            .WithTools<PackTools>(HostJson.Options)
            .WithTools<AssetTools>(HostJson.Options)
            .WithTools<UsageTools>(HostJson.Options)
            .WithResources<PackResources>()
            .WithRequestFilters(filters => filters.AddCallToolFilter(UnknownArgumentFilter.Apply));
    }
}
