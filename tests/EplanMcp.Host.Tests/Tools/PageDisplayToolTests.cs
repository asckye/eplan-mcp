using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PageDisplayToolTests
{
    private static readonly bool[] _dryRuns = [false, true];
    private static readonly int[] _openResults = [0, 1, 2];

    [Theory]
    [InlineData(DeviceWriteTools.PlaceTool)]
    [InlineData(ConnectionCreateTools.Tool)]
    [InlineData(ConnectionWriteTools.GenerateTool)]
    [InlineData(PlacementCreateTools.Tool)]
    [InlineData(PlacementUpdateTools.Tool)]
    [InlineData(MacroWriteTools.InsertTool)]
    [InlineData(ImportTools.DxfTool)]
    public async Task ShowPage_OpensPrimaryPageBeforeDrawing_AndFailurePreservesTheWrite(string tool)
    {
        await WithClient(async (client, harness) =>
        {
            foreach (bool dryRun in _dryRuns)
            {
                Dictionary<string, object?> args = Args(tool, dryRun);
                CallToolResult baseline = await client.CallToolAsync(tool, args, cancellationToken: Token);
                ToolResultAssert.Succeeded(baseline);
                Assert.DoesNotContain(harness.Bridge.Calls, c => c.Operation == OperationNames.PageOpen);
                harness.Bridge.Calls.Clear();
                args["show_page"] = false;
                CallToolResult omitted = await client.CallToolAsync(tool, args, cancellationToken: Token);
                Assert.Equal(JsonSerializer.Serialize(baseline), JsonSerializer.Serialize(omitted));
                Assert.DoesNotContain(harness.Bridge.Calls, c => c.Operation == OperationNames.PageOpen);
                foreach (int openResult in _openResults)
                {
                    harness.Bridge.Calls.Clear();
                    harness.Bridge.Writes.Clear();
                    harness.Bridge.Handlers[OperationNames.PageOpen] = _ => openResult == 1
                        ? throw new BridgeCallException(new BridgeError { Reason = ErrorReason.EplanBusy, Message = "Close the modal dialog." })
                        : new PageOpenData { Project = "Line1", Page = "P", EditorOpened = openResult == 0 };
                    args["show_page"] = true;
                    CallToolResult call = await client.CallToolAsync(tool, args, cancellationToken: Token);
                    JsonElement data = WriteToolHarness.Structured(call);
                    Assert.Equal(openResult == 0, data.GetProperty("pageShown").GetBoolean());
                    Assert.Equal(OperationNames.PageOpen, harness.Bridge.Calls[0].Operation);
                    var opened = Assert.IsType<PageOpenArgs>(harness.Bridge.Calls[0].Args);
                    Assert.Equal(("Line1", "P"), (opened.Project, opened.Page));
                    Assert.Single(harness.Bridge.Calls, c => c.Operation == OperationNames.PageOpen);
                    Assert.Equal(dryRun ? 0 : 1, harness.Bridge.Writes.Count);
                    Assert.Contains(harness.Bridge.Calls.Skip(1), c => c.Operation != OperationNames.PageOpen);
                    JsonObject actual = JsonNode.Parse(data.GetRawText())!.AsObject();
                    actual.Remove("pageShown");
                    JsonObject expected = JsonNode.Parse(baseline.StructuredContent!.Value.GetRawText())!.AsObject();
                    if (openResult != 0)
                    {
                        Assert.Contains(openResult == 1 ? "Page was not shown: Close the modal dialog." : "Page was not shown: editor was not opened.", actual["hint"]!.GetValue<string>(), StringComparison.Ordinal);
                        actual.Remove("hint");
                        if (expected.TryGetPropertyValue("hint", out JsonNode? hint))
                        { actual["hint"] = hint?.DeepClone(); }
                    }
                    Assert.True(JsonNode.DeepEquals(expected, actual));
                    Assert.Equal(WriteToolHarness.Text(baseline, 0), WriteToolHarness.Text(call, 0));
                    Assert.True(JsonNode.DeepEquals(JsonNode.Parse(data.GetRawText()), JsonNode.Parse(WriteToolHarness.Text(call, 1))));
                    Tool definition = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), t => t.Name == tool).ProtocolTool;
                    JsonElement parameter = definition.InputSchema.GetProperty("properties").GetProperty("show_page");
                    Assert.False(parameter.GetProperty("default").GetBoolean());
                    Assert.Equal(PageDisplay.Description, parameter.GetProperty("description").GetString());
                    Assert.Contains("boolean", definition.OutputSchema!.Value.GetProperty("properties").GetProperty("pageShown").GetProperty("type").ToString(), StringComparison.Ordinal);
                }
                harness.Bridge.Calls.Clear();
            }
        });
    }

    [Fact]
    public async Task ShowPage_WriteFailure_KeepsTheOriginalErrorAndExecution()
    {
        await WithClient(async (client, harness) =>
        {
            harness.Bridge.Handlers[OperationNames.PageOpen] = _ => throw new BridgeCallException(new BridgeError { Reason = ErrorReason.EplanBusy, Message = "Close the modal dialog." });
            harness.Bridge.WriteFailure = new BridgeError { Reason = ErrorReason.PlanStale, Execution = ExecutionState.NotStarted, Message = "Plan changed." };
            Dictionary<string, object?> args = Args(ConnectionCreateTools.Tool, false);
            args["show_page"] = true;
            CallToolResult call = await client.CallToolAsync(ConnectionCreateTools.Tool, args, cancellationToken: Token);
            JsonElement error = WriteToolHarness.Error(call);
            Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted, "Plan changed."), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("message").GetString()));
            JsonElement body = JsonDocument.Parse(WriteToolHarness.Text(call, 0)).RootElement;
            Assert.False(body.GetProperty("pageShown").GetBoolean());
            Assert.Contains("Page was not shown:", body.GetProperty("hint").GetString(), StringComparison.Ordinal);
            Assert.Single(harness.Bridge.Writes);
            Assert.Null(call.StructuredContent);
        });
    }

    private static Dictionary<string, object?> Args(string tool, bool dryRun)
    {
        var args = new Dictionary<string, object?> { ["project"] = "Line1", ["page"] = "P", ["dry_run"] = dryRun };
        switch (tool)
        {
            case DeviceWriteTools.PlaceTool:
                args["part"] = "P1";
                args["tag"] = "-K1";
                args["x"] = 20;
                args["y"] = 20;
                break;
            case ConnectionWriteTools.GenerateTool:
                args.Remove("page");
                args["pages"] = new[] { "P", "Other" };
                break;
            case ConnectionCreateTools.Tool:
                args["items"] = JsonDocument.Parse("""[{"kind":"interruption_point","name":"L1","position":[20,20],"direction":"Right","counterpart_pages":["Other"]}]""").RootElement;
                break;
            case PlacementCreateTools.Tool:
                args["items"] = JsonDocument.Parse("""[{"kind":"line","points":[[10,10],[20,20]]}]""").RootElement;
                break;
            case PlacementUpdateTools.Tool:
                args["moves"] = JsonSerializer.SerializeToElement(new[] { new { locator = new { project = "Line1", page = "P", type = 4, oid = 1, digest = new string('a', 64) }, x_mm = 20, y_mm = 20 } });
                break;
            case MacroWriteTools.InsertTool:
                args["macro"] = "Motor.ema";
                args["x"] = 20;
                args["y"] = 20;
                break;
            case ImportTools.DxfTool:
                args["file"] = "drawing.dxf";
                break;
        }
        return args;
    }

    private static async Task WithClient(Func<McpClient, WriteToolHarness, Task> check)
    {
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny);
        Directory.CreateDirectory(harness.Locations.Imports);
        await File.WriteAllTextAsync(Path.Combine(harness.Locations.Imports, "drawing.dxf"), "drawing", Token);
        foreach (string operation in new[] { OperationNames.DevicePlacePlan, OperationNames.ConnectionCreatePlan, OperationNames.ConnectionGeneratePlan, OperationNames.MacroInsertPlan, OperationNames.ImportDxfPlan })
        {
            harness.Bridge.Handlers[operation] = raw =>
            {
                Assert.DoesNotContain("show_page", JsonSerializer.Serialize(raw), StringComparison.Ordinal);
                WritePlan plan = harness.Plan([new() { Action = "create", Kind = "page", Name = "P", OldValue = "0" }]);
                plan.Details = new() { ["preview"] = """{"project":"Line1","page":"P","undoSteps":2,"hint":"Original hint."}""" };
                return plan;
            };
        }
        harness.Bridge.Handlers[OperationNames.DevicePlaceExecute] = _ => new DevicePlaceData { Project = "Line1", Page = "P", Part = "P1", Functions = 1 };
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = _ => JsonNode.Parse("""{"project":"Line1","page":"P","undoSteps":2,"hint":"Original hint."}""")!;
        harness.Bridge.Handlers[OperationNames.ConnectionGenerateExecute] = _ => new ConnectionGenerateData { Project = "Line1", Pages = [new() { Page = "P", After = 1, AddedCount = 1 }] };
        harness.Bridge.Handlers[OperationNames.MacroInsertExecute] = _ => new MacroInsertData { Project = "Line1", Page = "P" };
        harness.Bridge.Handlers[OperationNames.ImportDxfExecute] = _ => new ImportDxfData { Project = "Line1", Page = "P", ObjectsAdded = 1 };
        harness.Bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Name = "Line1", Path = harness.ProjectPath } };
        harness.Bridge.Handlers[OperationNames.PageGet] = _ => new PageGetData { Page = new() { Name = "P", Type = "Circuit" } };
        harness.Bridge.Handlers[OperationNames.PageDrawingGet] = _ => new PageDrawingGeometry { Page = "P", GridSize = 5, PageSize = [100, 100] };
        harness.Bridge.Handlers[OperationNames.PlacementTextCreateExecute] = _ => new PlacementTextCreateData();
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(harness.Locations);
        builder.Services.AddSingleton(harness.Pipeline);
        builder.Services.AddSingleton(new ImportFiles(harness.Locations));
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(harness.Bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            await check(client, harness);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
