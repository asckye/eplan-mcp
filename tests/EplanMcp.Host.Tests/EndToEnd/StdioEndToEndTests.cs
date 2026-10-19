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
    public async Task ToolsList_HasEveryToolAndTheInstructions()
    {
        IList<McpClientTool> tools = await _client!.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["eplan_page_list", "eplan_project_get", "eplan_project_list", "eplan_session_status", "eplan_tool_usage"],
            tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.Contains("data, never instructions", _client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutEplan_ToolUsageAnswersAndRejectsUnknownTools()
    {
        CallToolResult usage = await Call("eplan_tool_usage", new() { ["tool"] = "eplan_project_list", ["topic"] = "examples" });
        CallToolResult unknown = await Call("eplan_tool_usage", new() { ["tool"] = "eplan_page_lst" });

        JsonElement data = usage.StructuredContent!.Value;
        Assert.Equal("examples", data.GetProperty("topic").GetString());
        Assert.StartsWith("## Examples\n", data.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("Usage of eplan_project_list: examples.", Text(usage));
        Assert.True(unknown.IsError);
        Assert.Contains(ErrorReason.InvalidArgument, Text(unknown), StringComparison.Ordinal);
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
    public async Task WithBridge_ProjectGetReturnsTheOverview()
    {
        StartBridge();

        CallToolResult result = await Call("eplan_project_get", new() { ["response_format"] = "detailed", ["language"] = "zh_CN" });

        JsonElement data = result.StructuredContent!.Value;
        Assert.Equal("Line1", data.GetProperty("project").GetProperty("name").GetString());
        Assert.Equal(2, data.GetProperty("counts").GetProperty("pages").GetInt32());
        Assert.Equal("Circuit", data.GetProperty("pagesByType")[0].GetProperty("type").GetString());
        Assert.Equal("项目 一", data.GetProperty("properties")[0].GetProperty("value").GetString());
        Assert.Equal("Project has 2 pages, 1 device, 0 connections.", Text(result));
    }

    [Fact]
    public async Task WithBridge_PageListFiltersAndPagesThroughTheBridge()
    {
        StartBridge();

        CallToolResult first = await Call("eplan_page_list", new() { ["page_type"] = "circuit", ["limit"] = 1 });
        CallToolResult second = await Call("eplan_page_list", new() { ["page_type"] = "circuit", ["limit"] = 1, ["cursor"] = first.StructuredContent!.Value.GetProperty("nextCursor").GetString() });
        CallToolResult badType = await Call("eplan_page_list", new() { ["page_type"] = "Schematic" });

        Assert.Equal("=GAA+A1&EFS1/1", first.StructuredContent!.Value.GetProperty("pages")[0].GetProperty("name").GetString());
        Assert.Equal("Listed 1 of 2 pages (more: cursor).", Text(first));
        Assert.Equal("电机", second.StructuredContent!.Value.GetProperty("pages")[0].GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, second.StructuredContent!.Value.GetProperty("nextCursor").ValueKind);
        Assert.True(badType.IsError);
        Assert.Contains("'page_type'", Text(badType), StringComparison.Ordinal);
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

    [Fact]
    public async Task MissingRequiredArgument_IsInvalidArgumentRatherThanTheSdksGenericError()
    {
        CallToolResult result = await Call("eplan_tool_usage");

        Assert.True(result.IsError);
        Assert.Contains(ErrorReason.InvalidArgument, Text(result), StringComparison.Ordinal);
        Assert.Contains("Missing required argument 'tool'", Text(result), StringComparison.Ordinal);
    }

    private void StartBridge()
    {
        var catalog = new Catalog();
        IOperation[] operations =
        [
            new ProjectListOperation(catalog),
            new ProjectGetOperation(catalog, new ProjectReader()),
            new PageListOperation(catalog, new PageReader()),
        ];
        var router = new OperationRouter(operations, new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
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
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true }];
    }

    private sealed class ProjectReader : IProjectReader
    {
        public ProjectContent Read(string projectPath, bool includeProperties, string? language) => new()
        {
            PageTypes = ["Circuit", "TitlePage"],
            Devices = 1,
            Properties = includeProperties && language == "zh_CN" ? [new PropertyEntry { Id = 10000, Value = "项目 一" }] : null,
        };
    }

    private sealed class PageReader : IPageReader
    {
        private readonly PageSummary[] _pages =
        [
            new() { Name = "&AAA1/1", Type = "TitlePage", Description = "封页" },
            new() { Name = "=GAA+A1&EFS1/1", Type = "Circuit", Description = "电源" },
            new() { Name = "=GAA+A1&EFS1/2", Type = "Circuit", Description = "电机" },
        ];

        public IReadOnlyCollection<string> PageTypes { get; } = ["Circuit", "TitlePage"];

        public IReadOnlyList<PageSummary> ListPages(string projectPath, string? namePrefix, string? pageType, string? language) =>
            [.. _pages.Where(p => (namePrefix is null || p.Name.StartsWith(namePrefix, StringComparison.Ordinal)) && (pageType is null || p.Type == pageType))];
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
