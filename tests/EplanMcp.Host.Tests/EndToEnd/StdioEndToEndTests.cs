using System.IO.Pipes;
using System.Text.Json;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tests.Bridge;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
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
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-action-e2e-").FullName;
    private BridgeServer? _bridge;
    private McpClient? _client;

    public async ValueTask InitializeAsync()
    {
        File.WriteAllText(Path.Combine(_home, "config.json"), "{\"actionRun\":\"on\"}");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "eplanmcp",
            Command = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"),
            Arguments = ["serve"],
            EnvironmentVariables = new Dictionary<string, string?> { [PipeBridgeDiscovery.PrefixVariable] = _prefix, ["EPLANMCP_HOME"] = _home },
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
        Directory.Delete(_home, recursive: true);
    }

    [Fact]
    public async Task ToolsList_HasEveryToolAndTheInstructions()
    {
        IList<McpClientTool> tools = await _client!.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["eplan_action_run", "eplan_asset_get", "eplan_asset_search", "eplan_asset_update", "eplan_cabinet_bind", "eplan_cabinet_list", "eplan_cable_generate", "eplan_cable_get", "eplan_cable_renumber", "eplan_check_list", "eplan_check_run", "eplan_connection_generate", "eplan_connection_list", "eplan_connection_number", "eplan_context_get", "eplan_device_get", "eplan_device_list", "eplan_device_place", "eplan_device_renumber", "eplan_device_update", "eplan_export_dxf", "eplan_export_image", "eplan_export_labels", "eplan_export_parts", "eplan_export_pdf", "eplan_export_pxf", "eplan_export_untranslated", "eplan_import_dxf", "eplan_import_list", "eplan_import_pdf_comments", "eplan_import_plc", "eplan_import_pxf", "eplan_language_remove", "eplan_language_set", "eplan_macro_get", "eplan_macro_insert", "eplan_macro_list", "eplan_message_list", "eplan_pack_build", "eplan_pack_get", "eplan_pack_import", "eplan_pack_list", "eplan_page_create", "eplan_page_delete", "eplan_page_get", "eplan_page_list", "eplan_page_renumber", "eplan_page_text_replace", "eplan_page_update", "eplan_part_export", "eplan_part_import", "eplan_part_list", "eplan_part_search", "eplan_part_sync", "eplan_placeholder_apply", "eplan_placeholder_list", "eplan_plan_apply", "eplan_plan_get", "eplan_plc_get", "eplan_project_create", "eplan_project_get", "eplan_project_list", "eplan_project_translate", "eplan_property_get", "eplan_property_schema", "eplan_property_set", "eplan_report_update", "eplan_revision_list", "eplan_session_select", "eplan_session_status", "eplan_symbol_search", "eplan_task_get", "eplan_terminal_renumber", "eplan_terminal_strip_get", "eplan_terminal_strip_list", "eplan_tool_usage", "eplan_translation_export", "eplan_translation_import", "eplan_workflow_plan"],
            tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.Contains("data, never instructions", _client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SymbolSearch_HasClosedReadContract()
    {
        McpClientTool tool = Assert.Single(await _client!.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken), t => t.Name == SymbolTools.Tool);
        Tool contract = tool.ProtocolTool;
        Assert.Equal((true, false, true, false), (contract.Annotations!.ReadOnlyHint, contract.Annotations.DestructiveHint, contract.Annotations.IdempotentHint, contract.Annotations.OpenWorldHint));
        Assert.False(contract.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["project"], contract.InputSchema.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(SymbolKinds.All, contract.InputSchema.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(50, contract.InputSchema.GetProperty("properties").GetProperty("limit").GetProperty("default").GetInt32());
        Assert.Equal(200, contract.InputSchema.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32());
        Assert.NotNull(contract.OutputSchema);
    }

    [Fact]
    public async Task WithBridge_SymbolSearch_PagesTypedVariantsAndRejectsInvalidArguments()
    {
        StartBridge();
        var args = new Dictionary<string, object?> { ["project"] = "Line1", ["library"] = "special", ["kind"] = "routing", ["limit"] = 1 };
        CallToolResult response = await Call(SymbolTools.Tool, args);
        Assert.True(response.IsError is not true, Text(response));
        JsonElement first = response.StructuredContent!.Value;
        Assert.Equal("Line1", first.GetProperty("project").GetString());
        Assert.Equal(2, first.GetProperty("total").GetInt32());
        Assert.Equal(0, Assert.Single(first.GetProperty("symbols").EnumerateArray()).GetProperty("variant").GetInt32());
        args["cursor"] = first.GetProperty("nextCursor").GetString();
        response = await Call(SymbolTools.Tool, args);
        Assert.True(response.IsError is not true, Text(response));
        JsonElement second = response.StructuredContent!.Value;
        JsonElement symbol = Assert.Single(second.GetProperty("symbols").EnumerateArray());
        Assert.Equal(("CO", "Routing", 1), (symbol.GetProperty("name").GetString(), symbol.GetProperty("type").GetString(), symbol.GetProperty("variant").GetInt32()));
        Assert.Equal("Up", symbol.GetProperty("connectionPoints")[0].GetProperty("direction").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.False(second.TryGetProperty("execution", out _));

        args["initialize"] = true;
        Assert.True((await Call(SymbolTools.Tool, args)).IsError);
        Assert.Contains("'project'", Text(await Call(SymbolTools.Tool)), StringComparison.Ordinal);
        Assert.Contains(ErrorReason.ProjectNotOpen, Text(await Call(SymbolTools.Tool, new() { ["project"] = "missing" })), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithBridge_PropertyGet_UsesThePublicContractAndPreservesLanguageAndRefusals()
    {
        StartBridge();
        McpClientTool tool = Assert.Single(await _client!.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken), t => t.Name == PropertyGetTools.Tool);
        Assert.Equal((true, false, true, false), (tool.ProtocolTool.Annotations!.ReadOnlyHint,
            tool.ProtocolTool.Annotations.DestructiveHint, tool.ProtocolTool.Annotations.IdempotentHint, tool.ProtocolTool.Annotations.OpenWorldHint));
        Assert.True(JsonElement.DeepEquals(PropertyGetContract.Input, tool.ProtocolTool.InputSchema));
        Assert.True(JsonElement.DeepEquals(PropertyGetContract.Output, tool.ProtocolTool.OutputSchema!.Value));
        JsonElement data = PropertyGetToolTests.Data(await Call(PropertyGetTools.Tool, new()
        {
            ["queries"] = JsonSerializer.Deserialize<JsonElement>("""
                [{"target":{"project_object":true},"properties":[{"property_id":3010},{"property_id":10014},{"property_name":"PROJ_DRAWINGNUMBER"}]},
                 {"target":{"device_tag":"K1"},"properties":[{"property_name":"FUNC_TEXT"}]}]
                """),
            ["language"] = "zh_CN",
            ["response_format"] = "detailed",
        }));
        Assert.Equal("Line1", data.GetProperty("project").GetString());
        Assert.Equal(4, data.GetProperty("total").GetInt32());
        JsonElement[] rows = [.. data.GetProperty("items").EnumerateArray()];
        JsonElement text = Assert.Single(rows, r => r.GetProperty("query").GetInt32() == 1);
        Assert.Equal(("zh_CN", "explicit text", false), (text.GetProperty("language").GetString(), text.GetProperty("value").GetString(), text.GetProperty("fallback").GetBoolean()));
        Assert.Equal([3010, 10013, 10014], rows.Where(r => r.GetProperty("query").GetInt32() == 0).Select(r => r.GetProperty("property_id").GetInt32()));
        Assert.Equal(PropertyCatalog.ReadRefusal("Project", 3010), Assert.Single(rows, r => r.GetProperty("property_id").GetInt32() == 3010).GetProperty("reason").GetString());
        Assert.Equal("sensitive_text", Assert.Single(rows, r => r.GetProperty("property_id").GetInt32() == 10014).GetProperty("reason").GetString());
        Assert.DoesNotContain("private", data.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithBridge_PropertyGet_PagesAcrossRealBridgeContinuationBlocksWithoutLoss()
    {
        StartBridge();
        var args = new Dictionary<string, object?>
        {
            ["queries"] = JsonSerializer.Deserialize<JsonElement>("""
                [{"target":{"project_object":true},"properties":[{"property_id":10901},{"property_id":10013}]}]
                """),
            ["limit"] = 200,
            ["response_format"] = "detailed",
        };
        var rows = new List<JsonElement>();
        int pages = 0;
        do
        {
            CallToolResult call = await Call(PropertyGetTools.Tool, args);
            JsonElement data = PropertyGetToolTests.Data(call);
            Assert.InRange(ResultShaper.EstimateTokens(data.GetRawText()) + call.Content.Cast<TextContentBlock>().Sum(b => ResultShaper.EstimateTokens(b.Text)), 1, 20000);
            rows.AddRange(data.GetProperty("items").EnumerateArray());
            Assert.NotEmpty(data.GetProperty("items").EnumerateArray());
            args["cursor"] = data.GetProperty("cursor").GetString();
            if (args["cursor"] is null)
            {
                Assert.Equal(206, data.GetProperty("total").GetInt32());
            }
            Assert.InRange(++pages, 1, 20);
        }
        while (args["cursor"] is not null);
        Assert.True(pages > 1);
        Assert.Equal(206, rows.Count);
        Assert.Equal(10013, rows[0].GetProperty("property_id").GetInt32());
        Assert.Equal(Enumerable.Range(0, 205).Select(i => 10 + 2 * i), rows.Skip(1).Select(r => r.GetProperty("index").GetInt32()));
    }

    [Fact]
    public async Task PropertyGet_ValidationPrecedesConnectionAndBridgeShapeErrorsKeepPointers()
    {
        CallToolResult invalid = await Call(PropertyGetTools.Tool, new() { ["queries"] = Array.Empty<object>(), ["limit"] = "bad", ["extra"] = true });
        JsonElement error = PropertyGetRequestTests.Error(invalid);
        Assert.Equal(["/extra", "/limit", "/queries"], PropertyGetRequestTests.Violations(error).Select(v => v.Path));
        Assert.Null(_bridge);
        StartBridge();
        foreach (string target in new[] { "{\"project_object\":false}", "{\"projectObject\":true}", "{\"project_object\":true,\"unknown\":true}" })
        {
            JsonElement rejected = PropertyGetRequestTests.Error(await Call(PropertyGetTools.Tool, new()
            {
                ["queries"] = JsonSerializer.Deserialize<JsonElement>("[{\"target\":" + target + ",\"properties\":[{\"property_id\":10013}]}]"),
            }));
            Assert.Equal(ErrorReason.InvalidArgument, rejected.GetProperty("reason").GetString());
            Assert.Equal("/queries/0", rejected.GetProperty("metadata").GetProperty("pointer").GetString());
        }
        JsonElement ambiguous = PropertyGetRequestTests.Error(await Call(PropertyGetTools.Tool, new()
        {
            ["queries"] = JsonSerializer.Deserialize<JsonElement>("""[{"target":{"device_tag":"ambiguous"},"properties":[{"property_id":20011}]}]"""),
        }));
        Assert.Equal(ErrorReason.AmbiguousName, ambiguous.GetProperty("reason").GetString());
        Assert.Equal("/queries/0/target", ambiguous.GetProperty("metadata").GetProperty("pointer").GetString());
        Assert.Single(JsonSerializer.Deserialize<ObjectLocator[]>(ambiguous.GetProperty("metadata").GetProperty("candidates").GetString()!, HostJson.Options)!);
    }

    [Fact]
    public async Task WithoutEplan_PropertySchema_PublishesCatalogContractAndPagesIdenticalJsonCopies()
    {
        McpClientTool tool = Assert.Single(await _client!.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken), t => t.Name == PropertySchemaTools.Tool);
        Assert.Equal((true, false, true, false), (tool.ProtocolTool.Annotations!.ReadOnlyHint,
            tool.ProtocolTool.Annotations.DestructiveHint, tool.ProtocolTool.Annotations.IdempotentHint, tool.ProtocolTool.Annotations.OpenWorldHint));
        Assert.Equal("object", tool.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
        var args = new Dictionary<string, object?> { ["object_type"] = "Function", ["limit"] = 1, ["response_format"] = "detailed" };
        JsonElement first = PropertySchemaToolTests.Data(await Call(PropertySchemaTools.Tool, args));
        Assert.Equal("2026.0.3", first.GetProperty("catalog_version").GetString());
        JsonElement item = Assert.Single(first.GetProperty("items").EnumerateArray());
        Assert.Equal("readable", item.GetProperty("read").GetString());
        Assert.False(item.GetProperty("write").GetProperty("write_allowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("write").GetProperty("api_writable").ValueKind);
        Assert.All(item.GetProperty("definition").EnumerateObject(), fact => Assert.Equal(JsonValueKind.Null, fact.Value.ValueKind));
        args["cursor"] = first.GetProperty("cursor").GetString();
        JsonElement second = PropertySchemaToolTests.Data(await Call(PropertySchemaTools.Tool, args));
        Assert.NotEqual(item.GetProperty("property_id").GetInt32(), second.GetProperty("items")[0].GetProperty("property_id").GetInt32());
        args["object_type"] = "Page";
        PropertySchemaToolTests.Invalid(await Call(PropertySchemaTools.Tool, args));
        Assert.Null(_bridge);
    }

    [Fact]
    public async Task WithoutEplan_PropertySchema_RejectsTargetAndInvalidSelectorsAsStructuredToolErrors()
    {
        foreach (Dictionary<string, object?> args in new Dictionary<string, object?>[]
        {
            new() { ["object_type"] = "Function", ["target"] = new { device_tag = "-K1" } },
            new() { ["object_type"] = "Function", ["project"] = "Line1" },
            new() { ["object_type"] = "Function", ["properties"] = new[] { new { property_id = 20011, unknown = true } } },
            new() { ["object_type"] = "Function", ["properties"] = new[] { new { property_id = 20011, property_name = "FUNC_TEXT" } } },
            new() { ["object_type"] = "Function", ["properties"] = new[] { new { property_id = 20011 } }, ["name_contains"] = "TEXT" },
            new() { ["object_type"] = "Function", ["limit"] = 201, ["response_format"] = "invalid" },
        })
        {
            PropertySchemaToolTests.Invalid(await Call(PropertySchemaTools.Tool, args));
        }

        JsonElement data = PropertySchemaToolTests.Data(await Call(PropertySchemaTools.Tool,
            new() { ["object_type"] = "Project", ["name_contains"] = "PROJ_FULL_PROJECTPATH" }));
        Assert.Equal("path", Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("read").GetString());
        JsonElement usage = PropertySchemaToolTests.Data(await Call(UsageTools.UsageTool, new() { ["tool"] = PropertySchemaTools.Tool }));
        Assert.Contains("api_writable is null", usage.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Null(_bridge);
    }

    [Fact]
    public async Task WithBridge_ContextGet_ReturnsBothJsonCopies_ValidatesScopeAndKeepsReadOnlyShape()
    {
        StartBridge();
        CallToolResult result = await Call("eplan_context_get", new() { ["scope"] = new { pages = new List<string> { "+A1/1" } }, ["max_tokens"] = 20000 });
        ToolResultAssert.Succeeded(result);
        JsonElement data = result.StructuredContent!.Value;
        Assert.True(JsonElement.DeepEquals(data, JsonDocument.Parse(((TextContentBlock)result.Content[1]).Text).RootElement));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("contextRef").GetProperty("snapshotHash").ValueKind);
        Assert.Equal(2, data.GetProperty("objects").GetArrayLength());
        Assert.False(data.TryGetProperty("execution", out _));
        Assert.StartsWith("Returned context for 1 page;", ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_home, "workspaces")));
        CallToolResult invalid = await Call("eplan_context_get", new() { ["scope"] = new { depth = 2 }, ["include"] = new List<string> { "objects", "objects" } });
        Assert.Equal(ErrorReason.InvalidArgument, ContextToolTests.Error(invalid).GetProperty("reason").GetString());
        CallToolResult cabinet = await Call("eplan_context_get", new() { ["scope"] = new { }, ["cabinet"] = "panel" });
        Assert.Equal(ErrorReason.InvalidArgument, ContextToolTests.Error(cabinet).GetProperty("reason").GetString());
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
    public async Task ActionRun_MissingFilterIsRefusedBeforeAnyBridge_AndEntryUsageWorks()
    {
        CallToolResult invalid = await Call("eplan_action_run", new() { ["entry"] = "partsmanagementapi/EXPORTMANUFACTURERS", ["arguments"] = new { EXPORTFILE = "parts.xml" } });
        Assert.Contains(ErrorReason.InvalidArgument, Text(invalid), StringComparison.Ordinal);
        Assert.Contains("PROPERTYID", Text(invalid), StringComparison.Ordinal);
        Assert.Contains("PROPERTYVALUE", Text(invalid), StringComparison.Ordinal);
        CallToolResult usage = await Call("eplan_tool_usage", new() { ["tool"] = "eplan_action_run", ["topic"] = "partsmanagementapi/EXPORTMANUFACTURERS" });
        Assert.False(usage.IsError == true, Text(usage));
        Assert.Contains("22900", usage.StructuredContent!.Value.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("basic_project", "wrong.zw1")]
    [InlineData("backup", "wrong.zw9")]
    public async Task ProjectCreate_RejectsMismatchedMaterialsBeforeConnecting(string mode, string file)
    {
        CallToolResult result = await Call("eplan_project_create", new() { ["mode"] = mode, ["file"] = file, ["name"] = "Line1", ["dry_run"] = true });
        Assert.True(result.IsError);
        Assert.Contains(ErrorReason.InvalidArgument, Text(result), StringComparison.Ordinal);
        Assert.Contains(ExecutionState.NotStarted, Text(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("keep_old_names")]
    [InlineData("only_auto_cables")]
    [InlineData("start")]
    public async Task CableGenerate_RejectsUnsupportedSwitchesBeforeConnecting(string argument)
    {
        CallToolResult result = await Call("eplan_cable_generate", new()
        {
            ["project"] = "Line1",
            ["creation_scheme"] = "Standard",
            ["numbering_scheme"] = "Standard",
            ["selection_scheme"] = "Standard",
            [argument] = false,
        });
        Assert.True(result.IsError);
        Assert.Contains(ErrorReason.InvalidArgument, Text(result), StringComparison.Ordinal);
        Assert.Contains("Unknown argument '" + argument + "'", Text(result), StringComparison.Ordinal);
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

        Assert.True(status.StructuredContent!.Value.GetProperty("connected").GetBoolean(), status.StructuredContent.Value.GetRawText());
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
            new SymbolSearchOperation(catalog, new SymbolReader()),
            new PropertyReadOperation(new PropertyTestAdapter()),
            new PageListOperation(catalog, pages),
            new PageGetOperation(catalog, pages),
            new DeviceListOperation(catalog, devices),
            new DeviceGetOperation(catalog, devices),
            new ConnectionListOperation(catalog, devices, new SampleConnections()),
            new PartListOperation(catalog, new SampleParts()),
            new MessageListOperation(new SystemMessageReader()),
            new CheckListOperation(catalog, new ProjectMessageReader()),
            new ContextTestSnapshot(),
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

    private sealed class SymbolReader : ISymbolReader
    {
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) =>
            SymbolKinds.Matches(args, "SPECIAL", "CO", "routing")
                ? [.. Enumerable.Range(0, 2).Reverse().Select(i => new SymbolSearchItem { Library = "SPECIAL", Name = "CO", Kind = "routing", Type = "Routing", Variant = i, ConnectionPoints = [new() { Location = [0, 0], Direction = "Up" }] })]
                : [];
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
