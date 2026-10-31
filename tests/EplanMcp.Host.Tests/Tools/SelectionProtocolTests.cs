using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Contracts;
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
public sealed class SelectionProtocolTests
{
    [Fact]
    public async Task StreamProtocol_ClosedLocatorSchemaEnumsAndReadLevelSideEffect()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var bridge = new FakeBridgeClient();
        var locator = new DrawingLocator { Project = @"D:\scratch\copy", Page = "=A/1", Type = 17, Oid = 1, Digest = new string('a', 64) };
        bridge.Handlers[OperationNames.SelectionSet] = value => new SelectionData
        {
            Project = locator.Project,
            Page = locator.Page,
            Exact = true,
            Total = ((SelectionArgs)value!).Locators.Count,
            Objects = ((SelectionArgs)value!).Locators.Select(l => new ObjectSummary { Locator = l }).ToList()
        };
        bridge.Handlers[OperationNames.SelectionGet] = _ => new SelectionData { Project = locator.Project, Page = locator.Page, Total = 1, Objects = [new() { Locator = locator }], Fingerprint = "f1" };
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddEplanMcp().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(bridge);
        using IHost host = builder.Build();
        CancellationToken token = TestContext.Current.CancellationToken;
        await host.StartAsync(token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: token);
            var tools = await client.ListToolsAsync(cancellationToken: token);
            foreach (string name in new[] { SelectionTools.GetTool, SelectionTools.SetTool })
            {
                var tool = Assert.Single(tools, t => t.Name == name).ProtocolTool;
                Assert.Equal((true, false, true, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint, tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
                Assert.NotNull(tool.OutputSchema);
                Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
            }
            var set = tools.Single(t => t.Name == SelectionTools.SetTool).ProtocolTool;
            JsonElement schema = set.InputSchema.GetProperty("properties");
            Assert.Equal(["replace", "add", "clear"], schema.GetProperty("mode").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
            Assert.False(schema.GetProperty("locators").GetProperty("items").GetProperty("additionalProperties").GetBoolean());
            var data = ApiToolTests.Data(await client.CallToolAsync(SelectionTools.SetTool, new Dictionary<string, object?> { ["locators"] = new[] { locator }, ["show_page"] = true }, cancellationToken: token));
            Assert.True(data.GetProperty("exact").GetBoolean());
            Assert.Empty(bridge.Writes);
            var read = ApiToolTests.Data(await client.CallToolAsync(SelectionTools.GetTool, new Dictionary<string, object?>(), cancellationToken: token));
            Assert.Equal(locator.Digest, read.GetProperty("items")[0].GetProperty("locator").GetProperty("digest").GetString());
            int calls = bridge.Calls.Count;
            foreach (var args in new Dictionary<string, object?>[] { new() { ["locators"] = new[] { new { project = locator.Project, page = locator.Page, type = 17, oid = 1, digest = locator.Digest, extra = true } } },
                new() { ["mode"] = "unknown" }, new() { ["mode"] = "clear", ["typo"] = true }, new() { ["mode"] = "clear", ["locators"] = null },
                new() { ["locators"] = new[] { new { project = locator.Project, page = locator.Page, type = "17", oid = 1, digest = locator.Digest } } },
                new() { ["locators"] = new[] { new { project = locator.Project, page = locator.Page, type = 2147483648L, oid = 1, digest = locator.Digest } } },
                new() { ["locators"] = JsonDocument.Parse("""[{"project":"P","page":"1","type":17,"oid":1,"oid":2,"digest":"hash"}]""").RootElement } })
            { ApiToolTests.AssertError(await client.CallToolAsync(SelectionTools.SetTool, args, cancellationToken: token)); }
            Assert.Equal(calls, bridge.Calls.Count);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
}
