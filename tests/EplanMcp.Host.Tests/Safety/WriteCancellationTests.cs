using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using PipeOptions = System.IO.Pipes.PipeOptions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// A client that cancels a write tool call (MCP notifications/cancelled) stops the write in the Bridge, and the audit
/// records what became of it (M4 design 4.3, ADR-0010 section 2): the MCP SDK on both ends, the Host's services, a real
/// BridgeClient and named pipe, and Bridge.Core's router over a write that works until it is cancelled.
/// </summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))] // it builds the tools in a host of its own
public sealed class WriteCancellationTests : IAsyncDisposable
{
    private const string Tool = "test_write";
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-cancel-").FullName);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<string> _transactions = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _bridge;

    public WriteCancellationTests()
    {
        var plan = new WritePlan
        {
            Project = "Line1",
            ProjectPath = Path.Combine(_locations.DefaultScratch, "Line1"),
            Changes = [new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "reports" }],
            Fingerprint = "f1",
        };
        IOperation[] operations = [new Operation("test.plan", () => plan), new WriteOperation("test.execute", () => Execute())];
        var router = new OperationRouter(operations, new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".0123456789abcdef",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "0.1.0-test", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
            router,
            new NullLog());
        _server.Start();
        _bridge = new BridgeClient(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(20));
    }

    public async ValueTask DisposeAsync()
    {
        await _bridge.DisposeAsync();
        _server.Dispose();
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Fact]
    public async Task AWriteTheClientCancels_IsRolledBackInEplanAndAuditedAsCancelled()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var toServer = new Pipe();
        var toClient = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_locations);
        builder.Services.AddEplanMcp()
            .WithTools<TestWriteTools>(HostJson.Options)
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(_bridge);
        using IHost host = builder.Build();
        await host.StartAsync(cancellationToken);

        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: cancellationToken);
            var id = new RequestId("call-1");
            using var giveUp = new CancellationTokenSource();
            Task call = client.SendRequestAsync<CallToolRequestParams, CallToolResult>(RequestMethods.ToolsCall, new CallToolRequestParams { Name = Tool }, requestId: id, cancellationToken: giveUp.Token).AsTask();
            await _started.Task.WaitAsync(_patience, cancellationToken);

            // What a client sends when its user cancels the call; the SDK's client (2.2.0) does not send it when its token fires.
            await client.SendNotificationAsync(NotificationMethods.CancelledNotification, new CancelledNotificationParams { RequestId = id }, cancellationToken: cancellationToken);

            JsonObject result = await ResultRecordAsync(cancellationToken);
            Assert.Equal((ExecutionState.RolledBack, ErrorReason.Cancelled), (result["execution"]!.GetValue<string>(), result["reason"]!.GetValue<string>()));
            Assert.Equal(["Begin", "CloseUndo", "Rollback", "Dispose"], _transactions.ToArray());
            await giveUp.CancelAsync();
            Assert.IsAssignableFrom<OperationCanceledException>(await Record.ExceptionAsync(() => call)); // a cancelled request gets no reply
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>The write works until the Host's cancel reaches it, then stops at the safe point before the commit.</summary>
    private bool Execute() => TransactionRunner.Run(new Transactions(_transactions), Tool, () =>
    {
        OperationContext context = OperationContext.Current;
        _started.TrySetResult();
        SpinWait.SpinUntil(() => context.IsCancellationRequested, _patience);
        return true;
    }).Result;

    /// <summary>The audit's execution_result record, once the cancelled call has settled in the background.</summary>
    private async Task<JsonObject> ResultRecordAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_patience);
        while (true)
        {
            if (Directory.Exists(_locations.Audit)
                && Directory.EnumerateFiles(_locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())
                    .FirstOrDefault(r => r["kind"]!.GetValue<string>() == "execution_result") is { } record)
            {
                return record;
            }

            await Task.Delay(20, timeout.Token);
        }
    }

    [McpServerToolType]
    private sealed class TestWriteTools
    {
        [McpServerTool(Name = Tool, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("A write in scratch, for the test.")]
        public static async Task<string> WriteAsync(WritePipeline pipeline, IProgress<ProgressNotificationValue> progress, CancellationToken cancellationToken)
        {
            var request = new WriteRequest<object, object>(Tool, ToolTier.Write, "test.plan", "test.execute", new { pages = 1 }, plan => new { plan.Fingerprint }, DryRun: false, TimeSpan.FromSeconds(10));
            return (await pipeline.RunAsync<object, object, JsonObject>(request, progress, cancellationToken)).Execution;
        }
    }

    private sealed class Operation(string name, Func<object?> run) : IOperation
    {
        public string Name => name;

        public object ParseArgs(JToken? args) => new();

        public object? Execute(object args) => run();
    }

    private sealed class WriteOperation(string name, Func<object?> run) : IWriteOperation
    {
        public string Name => name;

        public object ParseArgs(JToken? args) => new();

        public object? Execute(object args) => run();
    }

    private sealed class Transactions(ConcurrentQueue<string> calls) : ITransactionScopeFactory
    {
        public ITransactionScope Begin(string undoDescription)
        {
            calls.Enqueue("Begin");
            return new Scope(calls);
        }

        private sealed class Scope(ConcurrentQueue<string> calls) : ITransactionScope
        {
            public void Commit() => calls.Enqueue("Commit");

            public void CloseUndo() => calls.Enqueue("CloseUndo");

            public void Rollback() => calls.Enqueue("Rollback");

            public void Dispose() => calls.Enqueue("Dispose");
        }
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
