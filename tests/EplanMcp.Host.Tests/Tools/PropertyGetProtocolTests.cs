using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tests.EndToEnd;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>Real MCP server/filters and both serializers with the real property.read over fake native resources.</summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PropertyGetProtocolTests : IDisposable
{
    private static readonly JsonSerializerSettings _bridgeJson = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };
    private static readonly string[] _invalidQueries =
    [
        """{"target":42,"properties":[{"property_id":10013}]}""",
        """{"target":{"projectObject":true},"properties":[{"property_id":10013}]}""",
        """{"target":{"project_object":true},"properties":[42]}""",
        """{"target":{"project_object":true},"properties":[{"property_name":"PROJ_DRAWINGNUMBER","unknown":null}]}""",
    ];
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-property-protocol-").FullName);
    private readonly FakeBridgeClient _bridge = new() { Status = new([new("fake-pipe", 7)], new() { Eplan = new() { ProcessId = 7 } }, null) };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public PropertyGetProtocolTests()
    {
        _bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Name = "Line1", Path = "X:/fixture/Line1" } };
        var operation = new PropertyReadOperation(new PropertyTestAdapter());
        _bridge.Handlers[OperationNames.PropertyRead] = args =>
        {
            try
            {
                object? result = operation.Execute(operation.ParseArgs(JToken.Parse(JsonSerializer.Serialize(args, HostJson.Options))));
                return JsonSerializer.Deserialize<PropertyReadData>(JsonConvert.SerializeObject(result, _bridgeJson), HostJson.Options)!;
            }
            catch (BridgeErrorException ex)
            {
                throw new BridgeCallException(ex.Error);
            }
        };
    }

    [Fact]
    public async Task PublicTool_ContractPagingContinuationAndProjectBinding_AreValidatedOverMcp()
    {
        await WithClient(async client =>
        {
            McpClientTool tool = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), t => t.Name == PropertyGetTools.Tool);
            Assert.True(JsonElement.DeepEquals(PropertyGetContract.Input, tool.ProtocolTool.InputSchema));
            Assert.True(JsonElement.DeepEquals(PropertyGetContract.Output, tool.ProtocolTool.OutputSchema!.Value));
            Assert.Equal((true, false, true, false), (tool.ProtocolTool.Annotations!.ReadOnlyHint,
                tool.ProtocolTool.Annotations.DestructiveHint, tool.ProtocolTool.Annotations.IdempotentHint, tool.ProtocolTool.Annotations.OpenWorldHint));
            var args = new Dictionary<string, object?>
            {
                ["queries"] = JsonSerializer.Deserialize<JsonElement>("""
                    [{"target":{"project_object":true},"properties":[{"property_id":10901},{"property_name":"PROJ_DRAWINGNUMBER"}]}]
                    """),
                ["limit"] = 200,
                ["response_format"] = "detailed",
            };
            var indexes = new List<int>();
            string? firstCursor = null;
            int pages = 0;
            do
            {
                CallToolResult call = await client.CallToolAsync(PropertyGetTools.Tool, args, cancellationToken: Token);
                JsonElement data = PropertyGetToolTests.Data(call);
                Assert.Equal("Line1", data.GetProperty("project").GetString());
                Assert.InRange(ResultShaper.EstimateTokens(data.GetRawText()) + call.Content.Cast<TextContentBlock>().Sum(b => ResultShaper.EstimateTokens(b.Text)), 1, 20000);
                indexes.AddRange(data.GetProperty("items").EnumerateArray().Where(r => r.GetProperty("index").ValueKind != JsonValueKind.Null).Select(r => r.GetProperty("index").GetInt32()));
                args["cursor"] = data.GetProperty("cursor").GetString();
                firstCursor ??= args["cursor"] as string;
                if (args["cursor"] is null)
                {
                    Assert.Equal(206, data.GetProperty("total").GetInt32());
                }
                Assert.InRange(++pages, 1, 20);
            }
            while (args["cursor"] is not null);
            Assert.True(pages > 1);
            Assert.Equal(Enumerable.Range(0, 205).Select(i => 10 + 2 * i), indexes);
            args["cursor"] = firstCursor;
            _bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Name = "Line1", Path = "X:/other/Line1" } };
            JsonElement error = PropertyGetRequestTests.Error(await client.CallToolAsync(PropertyGetTools.Tool, args, cancellationToken: Token));
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        });
    }

    [Fact]
    public async Task PublicTool_BridgeShapeAndBlockLimitFailures_AreStructuredErrors()
    {
        await WithClient(async client =>
        {
            foreach (string query in _invalidQueries)
            {
                JsonElement error = PropertyGetRequestTests.Error(await client.CallToolAsync(PropertyGetTools.Tool,
                    new Dictionary<string, object?> { ["queries"] = JsonSerializer.Deserialize<JsonElement>("[" + query + "]") }, cancellationToken: Token));
                Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
                Assert.Equal("/queries/0", error.GetProperty("metadata").GetProperty("pointer").GetString());
            }
            _bridge.Handlers[OperationNames.PropertyRead] = _ => new PropertyReadData
            {
                Items = [new("value", new("text", text: new string('x', 1024 * 1024))) { Target = new(projectObject: true), PropertyId = 10013 }],
            };
            JsonElement oversized = PropertyGetRequestTests.Error(await client.CallToolAsync(PropertyGetTools.Tool,
                new Dictionary<string, object?>
                {
                    ["queries"] = JsonSerializer.Deserialize<JsonElement>("""[{"target":{"project_object":true},"properties":[{"property_id":10013}]}]"""),
                }, cancellationToken: Token));
            Assert.Equal(ErrorReason.EplanOperationFailed, oversized.GetProperty("reason").GetString());
        });
    }

    [Fact]
    public async Task PublicTool_RawValidationPrecedesBridge_AndLanguageRefusalsAndAmbiguitySurviveBothSerializers()
    {
        await WithClient(async client =>
        {
            JsonElement invalid = PropertyGetRequestTests.Error(await client.CallToolAsync(PropertyGetTools.Tool,
                new Dictionary<string, object?> { ["queries"] = Array.Empty<object>(), ["limit"] = "bad" }, cancellationToken: Token));
            Assert.Equal(["/limit", "/queries"], PropertyGetRequestTests.Violations(invalid).Select(v => v.Path));
            Assert.Empty(_bridge.Calls);
            JsonElement data = PropertyGetToolTests.Data(await client.CallToolAsync(PropertyGetTools.Tool, new Dictionary<string, object?>
            {
                ["queries"] = JsonSerializer.Deserialize<JsonElement>("""
                    [{"target":{"device_tag":"K1"},"properties":[{"property_name":"FUNC_TEXT"},{"property_id":3010}]}]
                    """),
                ["language"] = "zh_CN",
                ["response_format"] = "detailed",
            }, cancellationToken: Token));
            JsonElement text = Assert.Single(data.GetProperty("items").EnumerateArray(), r => r.GetProperty("property_id").GetInt32() == 20011);
            Assert.Equal(("zh_CN", "explicit text", false), (text.GetProperty("language").GetString(), text.GetProperty("value").GetString(), text.GetProperty("fallback").GetBoolean()));
            JsonElement refused = Assert.Single(data.GetProperty("items").EnumerateArray(), r => r.GetProperty("status").GetString() == "read_refused");
            Assert.Equal(PropertyCatalog.ReadRefusal("Function", 3010), refused.GetProperty("reason").GetString());
            Assert.Equal(JsonValueKind.Null, refused.GetProperty("texts").ValueKind);
            JsonElement error = PropertyGetRequestTests.Error(await client.CallToolAsync(PropertyGetTools.Tool, new Dictionary<string, object?>
            {
                ["queries"] = JsonSerializer.Deserialize<JsonElement>("""[{"target":{"device_tag":"ambiguous"},"properties":[{"property_id":20011}]}]"""),
            }, cancellationToken: Token));
            Assert.Equal(ErrorReason.AmbiguousName, error.GetProperty("reason").GetString());
            Assert.Equal("/queries/0/target", error.GetProperty("metadata").GetProperty("pointer").GetString());
            Assert.Single(JsonSerializer.Deserialize<ObjectLocator[]>(error.GetProperty("metadata").GetProperty("candidates").GetString()!, HostJson.Options)!);
        });
    }

    private async Task WithClient(Func<McpClient, Task> check)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_locations);
        builder.Services.AddEplanMcp().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(_bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: Token);
            await check(client);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);
}
