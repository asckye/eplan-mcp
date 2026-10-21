using System.IO.Pipes;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// The EPLAN dialog channel over a real pipe to Bridge.Core's approval.ask, with a dialog that answers when the test
/// says so: the answer may take longer than an ordinary call may, and while it is open the Host's other calls to that
/// EPLAN wait and then get EPLAN_BUSY (M3 design 4.7).
/// </summary>
[Trait("Size", "Medium")]
public sealed class ApprovalDialogInteropTests : IAsyncDisposable
{
    private static readonly TimeSpan _queueWait = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(1);

    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly HeldDialog _dialog = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public ApprovalDialogInteropTests()
    {
        var router = new OperationRouter(
            [new ProjectListOperation(new NoProjects()), new ApprovalAskOperation(_dialog)],
            new InlineDispatcher(),
            null,
            new NullLog(),
            TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".0123456789abcdef",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "0.1.0-test", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3" } },
            router,
            new NullLog());
        _server.Start();
        _client = new BridgeClient(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance, queueTimeout: _queueWait, callTimeout: _callTimeout);
    }

    public async ValueTask DisposeAsync()
    {
        _dialog.Answer(ApprovalDecisions.Cancelled);
        await _client.DisposeAsync();
        _server.Dispose();
        _dialog.Dispose();
    }

    [Fact]
    public async Task AnAnswerAfterTheUsualCallTimeout_StillArrives_AndOtherCallsMeanwhileAreBusy()
    {
        var plan = new WritePlan { Project = "项目 一", ProjectPath = @"D:\项目\一", Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }], Fingerprint = "f1" };
        var question = new ApprovalQuestion("r1", "eplan_page_create", "h1", plan, [], DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1)) { Tier = ToolTier.Write, Eplan = null };

        // The channel asks only a Bridge it has shaken hands with; on a busy machine the first status may come too early.
        for (int i = 0; i < 50 && (await _client.GetStatusAsync(TestContext.Current.CancellationToken)).Handshake is null; i++)
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        Task<ApprovalAnswer?> asking = new EplanDialogChannel(_client, TimeProvider.System).AskAsync(question, TestContext.Current.CancellationToken);
        if (!_dialog.Shown.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken))
        {
            // Never hang on a question that did not reach the dialog (this held CI runs for up to an hour): fail with why.
            await asking;
            Assert.Fail("The question did not reach the dialog within 30 s.");
        }

        BridgeCallException busy = await Assert.ThrowsAsync<BridgeCallException>(() =>
            _client.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new ProjectListArgs(), TestContext.Current.CancellationToken));
        await Task.Delay(_callTimeout, TestContext.Current.CancellationToken);
        _dialog.Answer(ApprovalDecisions.Granted);
        ApprovalAnswer? answer = await asking;

        Assert.Equal(ErrorReason.EplanBusy, busy.Error.Reason);
        Assert.Equal(new ApprovalAnswer("r1", "f1", "h1", ApprovalDecision.Granted), answer);
        Assert.Contains("项目 一", _dialog.Question!.Summary, StringComparison.Ordinal);
    }

    /// <summary>Stands for the window: open until <see cref="Answer"/>.</summary>
    private sealed class HeldDialog : IApprovalDialog, IDisposable
    {
        private readonly ManualResetEventSlim _answered = new();
        private string _decision = ApprovalDecisions.Cancelled;

        public ManualResetEventSlim Shown { get; } = new();

        public ApprovalAskArgs? Question { get; private set; }

        public void Answer(string decision)
        {
            _decision = decision;
            _answered.Set();
        }

        public string Ask(ApprovalAskArgs question, TimeSpan timeout)
        {
            Question = question;
            Shown.Set();
            _answered.Wait(timeout);
            return _decision;
        }

        public void Dispose()
        {
            _answered.Dispose();
            Shown.Dispose();
        }
    }

    private sealed class NoProjects : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [];
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
