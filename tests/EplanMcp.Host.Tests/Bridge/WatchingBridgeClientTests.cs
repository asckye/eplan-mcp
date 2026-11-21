using EplanMcp.Contracts;
using EplanMcp.Host.Addin;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Addin;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// ADR-0063 decision 6: a call that finds EPLAN busy gets one look at EPLAN's windows. The system messages window is
/// closed and audited, another window is named, and the call is never repeated.
/// </summary>
[Trait("Size", "Small")]
public sealed class WatchingBridgeClientTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-watch-").FullName;
    private readonly FakeEplan _eplan = new();
    private readonly BusyBridge _inner = new();

    public WatchingBridgeClientTests()
    {
        _eplan.Registered["Electric P8"] = @"C:\app\bridge\EplanMcp.Bridge.Eplan.dll";
        _inner.ProcessId = _eplan.Start("EPLAN.exe", "/Variant:\"Electric P8\"").Id;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Busy_BehindTheSystemMessagesWindow_ClosesIt_AuditsIt_AndAsksForAnotherCall()
    {
        _eplan.Dialogs.Add(EplanTestWindows.SystemMessages);

        BridgeCallException busy = await Assert.ThrowsAsync<BridgeCallException>(() => Client().InvokeAsync<object, object>("page.list", new(), CancellationToken.None));

        Assert.Equal(ErrorReason.EplanBusy, busy.Error.Reason);
        Assert.Contains("closed EPLAN's system messages window", busy.Error.Message, StringComparison.Ordinal);
        Assert.EndsWith("Call again.", busy.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_eplan.Dialogs);
        Assert.Equal(1, _inner.Calls);
        string audit = string.Concat(Directory.GetFiles(Path.Combine(_root, "audit"), "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("\"kind\":\"eplan_window_closed\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"trigger\":\"page.list\"", audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Busy_BehindAnotherDialog_NamesIt_AndClosesNothing()
    {
        _eplan.Dialogs.Add(EplanTestWindows.SavePrompt);

        BridgeCallException busy = await Assert.ThrowsAsync<BridgeCallException>(() => Client().InvokeAsync<object, object>("page.list", new(), CancellationToken.None));

        Assert.Contains(EplanTestWindows.SavePrompt.ToString(), busy.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Call again.", busy.Error.Message, StringComparison.Ordinal);
        Assert.Single(_eplan.Dialogs);
    }

    [Fact]
    public async Task OnReport_TheSystemMessagesWindowIsOnlyNamed()
    {
        _eplan.Dialogs.Add(EplanTestWindows.SystemMessages);

        BridgeCallException busy = await Assert.ThrowsAsync<BridgeCallException>(() => Client(EplanWindowWatch.Report).InvokeAsync<object, object>("page.list", new(), CancellationToken.None));

        Assert.Contains(EplanTestWindows.SystemMessages.ToString(), busy.Error.Message, StringComparison.Ordinal);
        Assert.Single(_eplan.Dialogs);
    }

    [Fact]
    public async Task AnotherError_IsPassedOnUntouched()
    {
        _inner.Error = new BridgeError { Reason = ErrorReason.Timeout, Message = "slow" };
        _eplan.Dialogs.Add(EplanTestWindows.SystemMessages);

        BridgeCallException failed = await Assert.ThrowsAsync<BridgeCallException>(() => Client().InvokeAsync<object, object>("page.list", new(), CancellationToken.None));

        Assert.Equal("slow", failed.Error.Message);
        Assert.Single(_eplan.Dialogs);
    }

    private WatchingBridgeClient Client(EplanWindowWatch mode = EplanWindowWatch.Close)
    {
        HostConfig config = HostConfig.Defaults(new HostDataLocations(_root)) with { EplanWindowWatch = mode };
        var watch = new WindowWatch(_eplan, new AuditLog(Path.Combine(_root, "audit"), TimeProvider.System), config, NullLogger<WindowWatch>.Instance);
        return new WatchingBridgeClient(_inner, watch);
    }

    private sealed class BusyBridge : IBridgeClient
    {
        public int ProcessId { get; set; }

        public int Calls { get; private set; }

        public BridgeError Error { get; set; } = new() { Reason = ErrorReason.EplanBusy, Message = "EPLAN shows a dialog.", Retryable = true };

        public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => GetStatusAsync(false, cancellationToken);

        public Task<BridgeStatus> GetStatusAsync(bool refresh, CancellationToken cancellationToken) =>
            Task.FromResult(new BridgeStatus([], new HandshakeResult { Eplan = new() { ProcessId = ProcessId } }, null));

        public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken) =>
            InvokeAsync<TArgs, TData>(operation, args, TimeSpan.Zero, null, cancellationToken);

        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            throw new BridgeCallException(new BridgeError { Reason = Error.Reason, Message = Error.Message, Retryable = Error.Retryable });
        }

        public Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
