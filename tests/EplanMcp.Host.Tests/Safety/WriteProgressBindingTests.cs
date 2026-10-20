using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// What write tools rely on (ADR-0011 section 2, M4 design 4.3): a tool method's <see cref="IProgress{T}"/> parameter is
/// bound by the SDK, stays out of the input schema, and carries the wait for the engineer and for EPLAN to a client that
/// sent a progress token.
/// The Host's own services in process, over in-memory streams, with a fake Bridge and approval channel.
/// </summary>
[Trait("Size", "Medium")]
public sealed class WriteProgressBindingTests : IDisposable
{
    private const string Tool = "test_write";

    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-progress-").FullName);
    private readonly ManualTime _time = new(DateTimeOffset.UtcNow);
    private readonly FakeBridgeClient _bridge = new();

    public WriteProgressBindingTests()
    {
        _bridge.Status = new BridgeStatus(
            [new BridgeEndpoint("p", Environment.ProcessId)],
            new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3", Variant = "Electric P8" } },
            null);
        _bridge.Handlers["test.plan"] = _ => new WritePlan
        {
            Project = "Line1",
            ProjectPath = Path.Combine(_locations.Root, "Projects", "Line1"),
            Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }],
            Fingerprint = "f1",
        };
        _bridge.Handlers["test.execute"] = _ => new JsonObject { ["created"] = 1 };
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public async Task AToolsProgressParameter_IsNotAnArgument_AndCarriesTheWaitToTheClient()
    {
        var progress = new RecordedProgress();
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted)
        {
            // The engineer answers only once both reports reached the client: the SDK sends each on its own, so one
            // still on its way when the result goes out would be dropped with the finished request.
            Wait = () =>
            {
                _time.Advance(TimeSpan.FromSeconds(31));
                SpinWait.SpinUntil(() => progress.Values.Count >= 2, TimeSpan.FromSeconds(10));
            },
        };

        (McpClientTool tool, CallToolResult result) = await CallAsync(dialog, progress);

        Assert.Equal("{}", JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]!.ToJsonString());
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(ExecutionState.Committed, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(
            ["Waiting for the engineer's approval in EPLAN (15 s of 120 s)", "Waiting for the engineer's approval in EPLAN (30 s of 120 s)"],
            await MessagesAsync(progress, 2, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheWaitForEplan_ReachesTheClientAsTheBridgesStepsAndHeartbeats()
    {
        var progress = new RecordedProgress();
        _bridge.DuringWrite = bridge =>
        {
            bridge!.Report(new ProgressNotification { RequestId = "r", Progress = 0, Total = 2, Message = "Exporting page 1 of 2" });
            _time.Advance(TimeSpan.FromSeconds(6));
            SpinWait.SpinUntil(() => progress.Values.Count >= 2, TimeSpan.FromSeconds(10)); // as above: before the result
        };

        (_, CallToolResult result) = await CallAsync(new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted), progress);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(
            ["Exporting page 1 of 2", "Exporting page 1 of 2 (5 s)"],
            await MessagesAsync(progress, 2, TestContext.Current.CancellationToken));
    }

    /// <summary>Calls the test tool through an MCP client, which sends a progress token, while the engineer is asked in <paramref name="dialog"/>.</summary>
    private async Task<(McpClientTool Tool, CallToolResult Result)> CallAsync(FakeApprovalChannel dialog, RecordedProgress progress)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var toServer = new Pipe();
        var toClient = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_locations).AddSingleton<TimeProvider>(_time);
        builder.Services.AddSingleton(new HostConfig([_locations.DefaultScratch], OutsideScratchWrites.Approve));
        builder.Services.AddEplanMcp()
            .WithTools<TestWriteTools>(HostJson.Options)
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(_bridge);
        builder.Services.RemoveAll<IApprovalChannel>();
        builder.Services.AddSingleton<IApprovalChannel>(dialog);
        using IHost host = builder.Build();
        await host.StartAsync(cancellationToken);

        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: cancellationToken);
            McpClientTool tool = (await client.ListToolsAsync(cancellationToken: cancellationToken)).Single(t => t.Name == Tool);
            return (tool, await client.CallToolAsync(Tool, new Dictionary<string, object?>(), progress, cancellationToken: cancellationToken));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// The messages, sorted, once <paramref name="count"/> have arrived: the SDK sends each report on its own, so two
    /// made at once (as the manual clock does) may overtake each other or the result. Fifteen seconds apart they do not.
    /// </summary>
    private static async Task<IEnumerable<string?>> MessagesAsync(RecordedProgress progress, int count, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (progress.Values.Count < count && !timeout.IsCancellationRequested)
        {
            await Task.Delay(20, CancellationToken.None);
        }

        return progress.Values.Select(v => v.Message).Order(StringComparer.Ordinal);
    }

    [McpServerToolType]
    private sealed class TestWriteTools
    {
        [McpServerTool(Name = Tool, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("A write outside scratch, for the test.")]
        public static async Task<string> WriteAsync(WritePipeline pipeline, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken)
        {
            var request = new WriteRequest<object, object>(Tool, ToolTier.Write, "test.plan", "test.execute", new { pages = 1 }, plan => new { plan.Fingerprint }, DryRun: false, TimeSpan.FromSeconds(60));
            return (await pipeline.RunAsync<object, object, JsonObject>(request, progress, cancellationToken)).Execution;
        }
    }
}
