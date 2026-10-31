using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class ObjectReadProtocolTests
{
    [Fact]
    public async Task FullProfile_ReadOnlyToolsAreDiscoverableAndCallable_CoreExcludesBoth()
    {
        foreach (ToolProfile profile in new[] { ToolProfile.Full, ToolProfile.Core })
        {
            string root = Directory.CreateTempSubdirectory("eplanmcp-object-protocol-").FullName;
            var incoming = new Pipe();
            var outgoing = new Pipe();
            HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new HostDataLocations(root));
            builder.Services.AddSingleton(new ToolProfileChoice(profile));
            builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
            builder.Services.AddSingleton<IBridgeClient>(ObjectReadToolTests.Bridge());
            using IHost host = builder.Build();
            CancellationToken token = TestContext.Current.CancellationToken;
            try
            {
                await host.StartAsync(token);
                await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: token);
                var tools = await client.ListToolsAsync(cancellationToken: token);
                foreach (string name in new[] { ObjectReadTools.FindTool, ObjectReadTools.GetTool })
                {
                    Assert.Equal(profile == ToolProfile.Full, tools.Any(t => t.Name == name));
                    if (profile == ToolProfile.Core)
                    { continue; }
                    McpClientTool tool = Assert.Single(tools, t => t.Name == name);
                    Assert.Equal((true, false, true, false), (tool.ProtocolTool.Annotations!.ReadOnlyHint, tool.ProtocolTool.Annotations.DestructiveHint, tool.ProtocolTool.Annotations.IdempotentHint, tool.ProtocolTool.Annotations.OpenWorldHint));
                    Assert.NotNull(tool.ProtocolTool.OutputSchema);
                    Assert.Equal(EplanMcp.Contracts.ObjectReadTypes.Types.Count, tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("object_type").GetProperty("enum").GetArrayLength());
                }
                if (profile == ToolProfile.Core)
                { continue; }
                JsonElement found = PropertyGetToolTests.Data(await client.CallToolAsync(ObjectReadTools.FindTool, new Dictionary<string, object?> { ["object_type"] = "Page", ["limit"] = 1 }, cancellationToken: token));
                JsonElement get = PropertyGetToolTests.Data(await client.CallToolAsync(ObjectReadTools.GetTool, new Dictionary<string, object?>
                { ["object_type"] = "Page", ["locator"] = found.GetProperty("items")[0].GetProperty("locator"), ["language"] = "zh_CN" }, cancellationToken: token));
                Assert.Single(get.GetProperty("items").EnumerateArray());
                JsonElement invalid = PropertyGetRequestTests.Error(await client.CallToolAsync(ObjectReadTools.GetTool, new Dictionary<string, object?> { ["object_type"] = "Page", ["locator"] = 42, ["limit"] = "1" }, cancellationToken: token));
                Assert.Equal("INVALID_ARGUMENT", invalid.GetProperty("reason").GetString());
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
