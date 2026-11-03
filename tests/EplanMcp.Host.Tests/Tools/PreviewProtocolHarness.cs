using System.IO.Pipelines;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>The real MCP transport and argument binder over a fake Bridge.</summary>
internal static class PreviewProtocolHarness
{
    internal static async Task RunAsync(WriteToolHarness harness, Func<McpClient, Task> check, Action<IServiceCollection>? configure = null)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(harness.Locations);
        builder.Services.AddSingleton(harness.Pipeline);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(harness.Bridge);
        configure?.Invoke(builder.Services);
        using IHost host = builder.Build();
        await host.StartAsync(token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: token);
            await check(client);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
