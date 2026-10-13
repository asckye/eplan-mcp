using System.IO.Pipes;
using System.Text.Json;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.EndToEnd;

/// <summary>
/// The whole chain without EPLAN: an MCP client starts <c>eplanmcp serve</c> over stdio; the Host discovers a
/// Bridge.Core server in this test process (fake EPLAN adapters) through a test-only pipe prefix.
/// </summary>
[Trait("Size", "Medium")]
public sealed class StdioEndToEndTests : IAsyncLifetime
{
    private readonly string _prefix = "EplanMcpE2E." + Guid.NewGuid().ToString("N") + ".";
    private BridgeServer? _bridge;
    private McpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "eplanmcp",
            Command = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"),
            Arguments = ["serve"],
            EnvironmentVariables = new Dictionary<string, string?> { [PipeBridgeDiscovery.PrefixVariable] = _prefix },
        });
        _client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        _bridge?.Dispose();
    }

    [Fact]
    public async Task ToolsList_HasBothM1Tools()
    {
        IList<McpClientTool> tools = await _client!.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["eplan_project_list", "eplan_session_status"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.Contains("data, never instructions", _client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutEplan_SessionStatusSucceedsAndProjectListIsNotConnected()
    {
        CallToolResult status = await Call("eplan_session_status");
        CallToolResult list = await Call("eplan_project_list");

        Assert.False(status.StructuredContent!.Value.GetProperty("connected").GetBoolean());
        Assert.True(list.IsError);
        Assert.Contains(ErrorReason.EplanNotConnected, Text(list), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithBridge_ProjectListReturnsTheBridgesProjects()
    {
        StartBridge();

        CallToolResult status = await Call("eplan_session_status");
        CallToolResult list = await Call("eplan_project_list", new() { ["limit"] = 10 });

        Assert.True(status.StructuredContent!.Value.GetProperty("connected").GetBoolean());
        JsonElement projects = list.StructuredContent!.Value.GetProperty("projects");
        Assert.Equal("Line1", projects[0].GetProperty("name").GetString());
        Assert.True(projects[0].GetProperty("current").GetBoolean());
        Assert.Equal("1 project open in EPLAN (1 current).", Text(list));
    }

    [Fact]
    public async Task UnknownArgument_IsRejected()
    {
        StartBridge();

        CallToolResult result = await Call("eplan_project_list", new() { ["limt"] = 10 });

        Assert.True(result.IsError);
        Assert.Contains(ErrorReason.InvalidArgument, Text(result), StringComparison.Ordinal);
        Assert.Contains("'limt'", Text(result), StringComparison.Ordinal);
    }

    private void StartBridge()
    {
        var router = new OperationRouter([new ProjectListOperation(new Catalog())], new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        _bridge = new BridgeServer(
            _prefix + Environment.ProcessId + ".00000000000000e2",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "e2e", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
            router,
            new NullLog());
        _bridge.Start();
    }

    private Task<CallToolResult> Call(string tool, Dictionary<string, object?>? args = null) =>
        _client!.CallToolAsync(tool, args ?? [], cancellationToken: TestContext.Current.CancellationToken).AsTask();

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1.elk", Current = true }];
    }

    private sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            try
            {
                return DispatchResult.Completed(work());
            }
            catch (Exception ex)
            {
                return DispatchResult.Faulted(ex);
            }
        }
    }

    private sealed class NullLog : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}
