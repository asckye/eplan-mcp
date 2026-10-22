using System.IO.Pipes;
using System.Text.Json;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tests.Bridge;
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
            ["eplan_cable_get", "eplan_cable_renumber", "eplan_check_list", "eplan_check_run", "eplan_connection_generate", "eplan_connection_list", "eplan_connection_number", "eplan_device_get", "eplan_device_list", "eplan_device_place", "eplan_device_renumber", "eplan_device_update", "eplan_export_dxf", "eplan_export_image", "eplan_export_labels", "eplan_export_parts", "eplan_export_pdf", "eplan_export_pxf", "eplan_export_untranslated", "eplan_import_dxf", "eplan_import_pxf", "eplan_language_remove", "eplan_language_set", "eplan_macro_get", "eplan_macro_insert", "eplan_macro_list", "eplan_message_list", "eplan_pack_build", "eplan_pack_get", "eplan_pack_import", "eplan_pack_list", "eplan_page_create", "eplan_page_delete", "eplan_page_get", "eplan_page_list", "eplan_page_renumber", "eplan_page_update", "eplan_part_export", "eplan_part_import", "eplan_part_list", "eplan_part_search", "eplan_part_sync", "eplan_placeholder_apply", "eplan_placeholder_list", "eplan_plc_get", "eplan_project_get", "eplan_project_list", "eplan_project_translate", "eplan_report_update", "eplan_revision_list", "eplan_session_select", "eplan_session_status", "eplan_terminal_renumber", "eplan_terminal_strip_get", "eplan_terminal_strip_list", "eplan_tool_usage", "eplan_translation_export"],
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
    public async Task WithBridge_DeviceListFindsAShortTagInEveryStructure()
    {
        StartBridge();

        CallToolResult list = await Call("eplan_device_list", new() { ["tag_contains"] = "-FC1", ["has_part"] = true });

        JsonElement devices = list.StructuredContent!.Value.GetProperty("devices");
        Assert.Equal(["=GAA+A1-FC1", "=GAB1+A1-FC1"], devices.EnumerateArray().Select(d => d.GetProperty("tag").GetString()));
        Assert.Equal("Listed 2 devices.", Text(list));
    }

    [Fact]
    public async Task WithBridge_DeviceGetAsksForAFullTagAndThenReturnsTheDevice()
    {
        StartBridge();

        CallToolResult ambiguous = await Call("eplan_device_get", new() { ["tag"] = "-FC1" });
        CallToolResult device = await Call("eplan_device_get", new() { ["tag"] = "=GAB1+A1-FC1", ["response_format"] = "detailed" });

        Assert.True(ambiguous.IsError);
        Assert.Contains(ErrorReason.AmbiguousName, Text(ambiguous), StringComparison.Ordinal);
        JsonElement data = device.StructuredContent!.Value;
        Assert.Equal("MotorOverloadSwitch", data.GetProperty("device").GetProperty("category").GetString());
        Assert.Equal(1, data.GetProperty("properties").GetArrayLength());
        Assert.Equal("Device found: MotorOverloadSwitch, 0 auxiliary functions, 1 part, 1 property.", Text(device));
    }

    [Fact]
    public async Task WithBridge_PartListSumsUpThePartsOfOneManufacturer()
    {
        StartBridge();

        CallToolResult result = await Call("eplan_part_list", new() { ["manufacturer"] = "SIE", ["language"] = "zh_CN" });

        JsonElement parts = result.StructuredContent!.Value.GetProperty("parts");
        Assert.Equal(["SIE.3RT2016-1BB41", "SIE.3RV2011-1JA10"], parts.EnumerateArray().Select(p => p.GetProperty("partNumber").GetString()));
        Assert.Equal("接触器，AC-3，4 kW/400 V", parts[0].GetProperty("description").GetString());
        Assert.Equal(3, parts[0].GetProperty("quantity").GetInt32());
        Assert.Equal("Listed 2 parts.", Text(result));
    }

    [Fact]
    public async Task WithBridge_ConnectionListFollowsADeviceAcrossPagesAndAsksForAFullTag()
    {
        StartBridge();

        CallToolResult byDevice = await Call("eplan_connection_list", new() { ["device"] = "=GAA+A1-FC1" });
        CallToolResult ambiguous = await Call("eplan_connection_list", new() { ["device"] = "-FC1" });
        CallToolResult neither = await Call("eplan_connection_list");

        JsonElement connections = byDevice.StructuredContent!.Value.GetProperty("connections");
        Assert.Equal(["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"], connections.EnumerateArray().Select(c => c.GetProperty("page").GetString()));
        Assert.Equal("=GAA+A1-MA1", connections[0].GetProperty("to").GetProperty("device").GetString());
        Assert.Equal("Listed 3 connections.", Text(byDevice));
        Assert.True(ambiguous.IsError);
        Assert.Contains(ErrorReason.AmbiguousName, Text(ambiguous), StringComparison.Ordinal);
        Assert.True(neither.IsError);
        Assert.Contains("'device' or 'page'", Text(neither), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithBridge_MessageListGivesTheNewestErrorsFirst()
    {
        StartBridge();

        CallToolResult errors = await Call("eplan_message_list", new() { ["min_level"] = "error" });
        CallToolResult badLevel = await Call("eplan_message_list", new() { ["min_level"] = "fatal" });

        JsonElement messages = errors.StructuredContent!.Value.GetProperty("messages");
        Assert.Equal(["脚本编译失败", "Datei nicht gefunden"], messages.EnumerateArray().Select(m => m.GetProperty("text").GetString()));
        Assert.Equal("2 messages at error level or above.", Text(errors));
        Assert.True(badLevel.IsError);
        Assert.Contains("'min_level'", Text(badLevel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithBridge_CheckListGivesTheStoredMessagesAndCounts()
    {
        StartBridge();

        CallToolResult result = await Call("eplan_check_list", new() { ["severity"] = "warning", ["include_done"] = true });

        JsonElement data = result.StructuredContent!.Value;
        Assert.Equal(["端子 -X1:1 没有连接", "已处理"], data.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("text").GetString()));
        Assert.Equal("=GAA+A1-X1:1", data.GetProperty("messages")[0].GetProperty("object").GetProperty("name").GetString());
        Assert.Equal("2 warnings, 1 hint from the last check.", Text(result));
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
    public async Task WithBridge_PageGetFindsThePageInAnyCaseAndReportsMissingOnes()
    {
        StartBridge();

        CallToolResult found = await Call("eplan_page_get", new() { ["page"] = "=gaa+a1&efs1/1" });
        CallToolResult missing = await Call("eplan_page_get", new() { ["page"] = "=GAA+A1&EFS1/9" });
        CallToolResult noPage = await Call("eplan_page_get", new() { ["response_format"] = "detailed" });

        JsonElement data = found.StructuredContent!.Value;
        Assert.Equal("=GAA+A1&EFS1/1", data.GetProperty("page").GetProperty("name").GetString());
        Assert.Equal("=GAA+A1-FC1", data.GetProperty("devices")[0].GetProperty("tag").GetString());
        Assert.Equal("Page found: Circuit, 1 device.", Text(found));
        Assert.True(missing.IsError);
        Assert.Contains(ErrorReason.PageNotFound, Text(missing), StringComparison.Ordinal);
        Assert.True(noPage.IsError);
        Assert.Contains("Missing required argument 'page'.", Text(noPage), StringComparison.Ordinal);
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
        var pages = new PageReader();
        var devices = new SampleDevices();
        IOperation[] operations =
        [
            new ProjectListOperation(catalog),
            new ProjectGetOperation(catalog, new ProjectReader()),
            new PageListOperation(catalog, pages),
            new PageGetOperation(catalog, pages),
            new DeviceListOperation(catalog, devices),
            new DeviceGetOperation(catalog, devices),
            new ConnectionListOperation(catalog, devices, new SampleConnections()),
            new PartListOperation(catalog, new SampleParts()),
            new MessageListOperation(new SystemMessageReader()),
            new CheckListOperation(catalog, new ProjectMessageReader()),
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

        public IReadOnlyList<string> PageNames(string projectPath) => [.. _pages.Select(p => p.Name)];

        public PageContent? ReadPage(string projectPath, string pageName, bool includeProperties, string? language) =>
            _pages.FirstOrDefault(p => p.Name == pageName) is { } page
                ? new PageContent
                {
                    Page = new PageDetail { Name = page.Name, Type = page.Type, TypeName = "多线原理图", Description = page.Description },
                    Functions =
                    [
                        new FunctionRecord { Name = "=GAA+A1-FC1", VisibleName = "-FC1", IsMain = true, Category = "CircuitBreaker" },
                        new FunctionRecord { Name = "=GAA+A1-FC1:1", VisibleName = "1", IsMain = false, Category = "NoContact" },
                    ],
                }
                : null;
    }

    /// <summary>EPLAN's enumeration order: oldest first.</summary>
    private sealed class SystemMessageReader : ISystemMessageReader
    {
        public IReadOnlyList<SystemMessageRecord> ReadAll() =>
        [
            new() { Level = 4, Index = "S085016", Text = "Datei nicht gefunden", Occurrences = 1 },
            new() { Level = 2, Index = "S000000", Text = "warning", Occurrences = 1 },
            new() { Level = 0, Index = "S000000", Text = "trace", Occurrences = 1 },
            new() { Level = 4, Index = "S000000", Text = "脚本编译失败", Occurrences = 2 },
        ];
    }

    private sealed class ProjectMessageReader : IProjectMessageReader
    {
        public IReadOnlyList<ProjectMessageRecord> ReadMessages(string projectPath) =>
        [
            new() { Id = 4025, Category = "Warning", Region = "Connections", Text = "端子 -X1:1 没有连接", ObjectKind = "Terminal", ObjectName = "=GAA+A1-X1:1" },
            new() { Id = 13001, Category = "Hint", Region = "PartsData", Text = "h" },
            new() { Id = 4026, Category = "Warning", Region = "Connections", Text = "已处理", Done = true },
        ];
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
