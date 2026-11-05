using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class GraphicEditProtocolTests
{
    private static readonly string[] _operations = ["scale", "order", "group", "ungroup", "explode_block", "dock_text", "undock_text", "corner_radius", "copy_format", "cut_off", "set_length", "stretch", "snap_to_grid", "chamfer", "fillet"];
    private static readonly string[] _scenarios = ["omitted", "preview", "execute", "denied", "invalid", "stale", "readback", "warning"];
    public static TheoryData<string, string> Cases => new(from operation in _operations
                                                          from scenario in _scenarios
                                                          select (operation, scenario));

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EachOperation_PreservesPreviewApprovalAndFailureResults(string operation, string scenario)
    {
        var approval = new FakeApprovalChannel("graphic", scenario == "denied" ? ApprovalDecision.Denied : ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(approval);
        int applied = 0;
        JsonObject State() => new()
        {
            ["project"] = "Line1",
            ["page"] = "P",
            ["operation"] = operation,
            ["fingerprint"] = "f1",
            ["predicted"] = new JsonArray(new JsonObject { ["drawingOrder"] = 65 }),
            ["newLocators"] = new JsonArray(),
            ["noOp"] = false,
        };
        harness.Bridge.Handlers[OperationNames.GraphicEditPlan] = _ => new WritePlan
        {
            Project = "Line1",
            ProjectPath = Path.Combine(harness.Locations.Root, "outside", "Line1"),
            Fingerprint = "f1",
            Changes = [new PlannedChange { Action = "edit", Kind = "placement", Name = "P:4/1", Property = operation, NewValue = "approved" }],
            Details = new() { ["preview"] = State().ToJsonString() },
        };
        harness.Bridge.Handlers[OperationNames.GraphicEditExecute] = args =>
        {
            JsonElement request = JsonSerializer.SerializeToElement(args).GetProperty("request");
            Assert.Equal(operation, request.GetProperty("operation").GetString());
            Assert.False(request.GetProperty("dry_run").GetBoolean());
            if (scenario is "stale" or "readback")
            { throw new BridgeCallException(new BridgeError { Reason = scenario == "stale" ? ErrorReason.PlanStale : ErrorReason.EplanOperationFailed, Message = "Injected verification failure.", Execution = scenario == "stale" ? ExecutionState.NotStarted : ExecutionState.RolledBack }); }
            applied++;
            JsonObject state = State();
            if (scenario == "warning")
            { state["warning"] = "Committed readback could not be confirmed."; }
            return state;
        };
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(harness.Locations);
        builder.Services.AddSingleton(harness.Pipeline);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(harness.Bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            var args = new Dictionary<string, object?>
            {
                ["project"] = "Line1",
                ["page"] = "P",
                ["operation"] = operation,
                ["locators"] = Enumerable.Range(1, operation is "group" or "dock_text" or "chamfer" or "fillet" ? 2 : 1).Select(Locator).ToArray(),
            };
            switch (operation)
            {
                case "scale":
                    args["scale_x"] = 2;
                    args["scale_y"] = 0.5;
                    args["origin_mm"] = new[] { 120, 40 };
                    break;
                case "order":
                    args["to"] = "front";
                    break;
                case "dock_text":
                    args["dock_direction"] = "bottom";
                    break;
                case "corner_radius":
                    args["radius_mm"] = 3;
                    break;
                case "copy_format":
                    args["source"] = Locator(3);
                    break;
                case "cut_off":
                    args["point_mm"] = new[] { 55, 40 };
                    break;
                case "set_length":
                    args["length_mm"] = 45;
                    break;
                case "stretch":
                    args["region_mm"] = new[] { 10, 10, 20, 20 };
                    args["offset_mm"] = new[] { 5, 3 };
                    break;
                case "chamfer":
                    args["distance_mm"] = 5;
                    break;
                case "fillet":
                    args["radius_mm"] = 5;
                    break;
            }
            if (scenario != "omitted")
            { args["dry_run"] = scenario == "preview"; }
            if (scenario == "invalid")
            { args["typo"] = true; }
            CallToolResult result = await client.CallToolAsync(GraphicEditTools.Tool, args, cancellationToken: Token);
            if (scenario is "invalid" or "denied" or "stale" or "readback")
            {
                JsonElement error = WriteToolHarness.Error(result);
                Assert.Equal(scenario switch { "invalid" => ErrorReason.InvalidArgument, "denied" => ErrorReason.ApprovalDenied, "stale" => ErrorReason.PlanStale, _ => ErrorReason.EplanOperationFailed }, error.GetProperty("reason").GetString());
                Assert.Equal(scenario == "readback" ? ExecutionState.RolledBack : ExecutionState.NotStarted, error.GetProperty("execution").GetString());
            }
            else
            {
                JsonElement data = WriteToolHarness.Structured(result);
                bool preview = scenario is "omitted" or "preview";
                Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, data.GetProperty("execution").GetString());
                Assert.Equal(preview, data.GetProperty("dryRun").GetBoolean());
                if (preview)
                { Assert.Contains("dry_run", data.GetProperty("hint").GetString(), StringComparison.Ordinal); }
                if (scenario == "warning")
                { Assert.Contains("readback", data.GetProperty("warning").GetString(), StringComparison.Ordinal); }
                Tool tool = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), t => t.Name == GraphicEditTools.Tool).ProtocolTool;
                Assert.True(PropertyGetContractTests.Accepts(tool.OutputSchema!.Value, data, tool.OutputSchema.Value));
                Assert.False(tool.Annotations!.DestructiveHint);
                Assert.False(tool.Annotations.ReadOnlyHint);
                Assert.False(tool.Annotations.IdempotentHint);
            }
            Assert.Equal(scenario is "execute" or "warning" ? 1 : 0, applied);
            Assert.Equal(scenario is "invalid" or "omitted" or "preview" ? 0 : 1, approval.Questions.Count);
            Assert.Equal(scenario is "execute" or "stale" or "readback" or "warning" ? 1 : 0, harness.Bridge.Writes.Count);
            if (scenario == "invalid")
            { Assert.Empty(harness.Bridge.Calls); }
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private static object Locator(int oid) => new { project = "Line1", page = "P", type = 4, oid, digest = new string('a', 64) };
    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
