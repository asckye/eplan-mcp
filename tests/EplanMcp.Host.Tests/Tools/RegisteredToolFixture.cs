using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

public sealed class RegisteredToolFixture : IDisposable
{
    private readonly ServiceProvider _services;

    public RegisteredToolFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(HostConfig.Defaults(HostDataLocations.FromEnvironment()) with
        {
            ActionRun = ActionRunSwitch.On,
            ApiTry = ActionRunSwitch.On,
            ActionTry = ActionRunSwitch.On,
        });
        services.AddEplanMcp();
        _services = services.BuildServiceProvider();
        Options = _services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        Tools = [.. Options.ToolCollection!.Select(t => t.ProtocolTool)];
        Registered = _services.GetRequiredService<ToolRegistry>().All;
    }

    internal McpServerOptions Options { get; }
    internal Tool[] Tools { get; }
    internal IReadOnlyList<McpServerTool> Registered { get; }

    public void Dispose() => _services.Dispose();
}
