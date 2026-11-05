using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using EplanMcp.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlacementSymbolProtocolTests
{
    [Theory]
    [InlineData(null, 4, false)]
    [InlineData(false, 4, true)]
    [InlineData(false, 99, false)]
    public async Task VariantThroughRegisteredTool_PreservesDefaultPreviewAndRejectsBeforeWrite(bool? dryRun, int variant, bool committed)
    {
        using var harness = new WriteToolHarness();
        var fake = new PlacementEditFixture(harness.ProjectPath);
        fake.Data["objects"]![0]!["symbol"] = new JObject { ["library"] = "IEC_symbol", ["name"] = "BP", ["variant"] = 0 };
        fake.Mutation = () => fake.Data["objects"]![0]!["symbol"]!["variant"] = variant;
        var reader = new Symbols();
        var plan = new PlacementEditOperation(fake, fake, false, fake.Backup, reader);
        var execute = new PlacementEditExecuteOperation(fake, fake, fake.Backup, reader);
        object Invoke(IOperation operation, object? args)
        {
            try
            {
                var result = (JObject)operation.Execute(JObject.Parse(JsonSerializer.Serialize(args, HostJson.Options)))!;
                return operation == plan ? result.ToObject<WritePlan>()! : JsonNode.Parse(result.ToString())!.AsObject();
            }
            catch (BridgeErrorException error) { throw new BridgeCallException(error.Error); }
        }
        harness.Bridge.Handlers[OperationNames.PlacementEditPlan] = a => Invoke(plan, a);
        harness.Bridge.Handlers[OperationNames.PlacementEditExecute] = a => Invoke(execute, a);
        var incoming = new Pipe();
        var outgoing = new Pipe();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(harness.Locations);
        builder.Services.AddSingleton(harness.Pipeline);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(harness.Bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            JObject input = fake.Input("set_variant");
            input["symbol_variant"] = variant;
            if (dryRun is not null)
            { input["dry_run"] = dryRun.Value; }
            CallToolResult result = await client.CallToolAsync(PlacementEditTools.Tool, JsonSerializer.Deserialize<Dictionary<string, object?>>(input.ToString())!, cancellationToken: Token);
            if (variant == 99)
            { Assert.Equal(ErrorReason.InvalidArgument, WriteToolHarness.Error(result).GetProperty("reason").GetString()); }
            else
            { Assert.Equal(committed ? ExecutionState.Committed : ExecutionState.NotStarted, WriteToolHarness.Structured(result).GetProperty("execution").GetString()); }
            Assert.Equal(committed ? 1 : 0, fake.Transactions);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
    private sealed class Symbols : ISymbolReader
    {
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => [new() { Library = "IEC_symbol", Name = "BP", Variant = 0 }, new() { Library = "IEC_symbol", Name = "BP", Variant = 4 }];
    }
    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
