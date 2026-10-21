using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using EplanMcp.Workbench.Protocol;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// The Workbench's end of the approval pipe (ADR-0014 decisions 2 and 3, SEC-22, SEC-24). It takes the pipe name of this
/// user and data root as its first instance, or serves nothing; the ACL admits the current user only and no network
/// logon. Each connection is one Host with one question: its process must run <c>eplanmcp.exe</c> beside the Workbench
/// (a guard against mismatches, not authentication), the handshake comes first and every message is read strictly. The
/// question waits in the <see cref="ApprovalQueue"/> until the engineer, the deadline, a cancel or the end of the
/// connection settles it; an approved write's connection stays open for the outcome, which approves nothing.
/// </summary>
internal sealed partial class ApprovalServer : IAsyncDisposable
{
    /// <summary>How long a Host may take for its handshake, then for its question, and to hang up after the answer.</summary>
    public static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The latest deadline a question may have: the Host's is at most 300 s away (ADR-0011 section 2), so one further
    /// off is not a valid question.
    /// </summary>
    public static readonly TimeSpan MaxDeadline = TimeSpan.FromMinutes(15);

    private const int InvalidRequest = -32600;
    private const int InvalidParams = -32602;

    private static readonly string _version = typeof(ApprovalServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    private readonly ApprovalQueue _queue;
    private readonly string _expectedClient;
    private readonly SecurityIdentifier? _owner;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _instances = [];
    private volatile bool _quitting;
    private int _disposed;

    /// <summary>The Workbench of this user and <paramref name="dataRoot"/>, serving the Host installed beside it.</summary>
    public ApprovalServer(ApprovalQueue queue, string dataRoot, TimeProvider clock, ILogger logger)
        : this(queue, WorkbenchPipeName.ForCurrentUser(dataRoot), ClientBeside(AppContext.BaseDirectory), clock, logger)
    {
    }

    /// <summary>
    /// For tests: their own pipe and client image, never from configuration, and the pipe's owner. A test that runs
    /// elevated, as on CI, gives its token's owner (Administrators), which the Host's <c>CurrentUserOnly</c> compares with;
    /// the Workbench itself always makes the user the owner.
    /// </summary>
    internal ApprovalServer(ApprovalQueue queue, string pipeName, string expectedClient, TimeProvider clock, ILogger logger, SecurityIdentifier? owner = null)
    {
        _queue = queue;
        PipeName = pipeName;
        _expectedClient = expectedClient;
        _owner = owner;
        _clock = clock;
        _logger = logger;
    }

    public string PipeName { get; }

    /// <summary><c>app\eplanmcp.exe</c> for the Workbench in <c>app\workbench\</c> (M5 design 4.1).</summary>
    public static string ClientBeside(string workbenchDirectory) =>
        Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(workbenchDirectory)) ?? workbenchDirectory, "eplanmcp.exe");

    /// <summary>Takes the pipe name and serves it; false, logged, when another program holds the name, and nothing is served.</summary>
    public bool Start()
    {
        // The first instance is made before the method's first await, so a name held elsewhere shows at once.
        Task first = InstanceAsync(firstInstance: true);
        if (first.Exception?.InnerException is { } taken)
        {
            LogNameTaken(PipeName, taken.Message);
            return false;
        }

        LogListening(PipeName);
        Track(first);
        return true;
    }

    /// <summary>
    /// Ends every waiting question as closed (DLG-11) and every connection. A Host takes a broken connection as closed
    /// too, so its answer may or may not get out first.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _quitting = true;
        foreach (Question question in _queue.Pending)
        {
            _queue.End(question, QuestionEnd.Closed);
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        Task[] instances;
        lock (_instances)
        {
            instances = [.. _instances];
        }

        await Task.WhenAll(instances).ConfigureAwait(false);
        _stop.Dispose();
    }

    /// <summary>
    /// One pipe instance: it waits for a Host, starts the next instance before serving, so the name stays held, and ends
    /// with its connection. Only the first may fail to be made, which <see cref="Start"/> reports.
    /// </summary>
    private async Task InstanceAsync(bool firstInstance)
    {
        using NamedPipeServerStream? pipe = Make(firstInstance);
        if (pipe is null)
        {
            return;
        }

        try
        {
            await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return;
        }
        catch (IOException)
        {
            Track(InstanceAsync(firstInstance: false)); // a client that left before it was accepted
            return;
        }

        Track(InstanceAsync(firstInstance: false));
        await ServeAsync(pipe).ConfigureAwait(false);
    }

    /// <summary>A new instance; for any but the first, null, logged, when none can be made.</summary>
    private NamedPipeServerStream? Make(bool firstInstance)
    {
        try
        {
            return Create(firstInstance);
        }
        catch (Exception ex) when (!firstInstance && ex is UnauthorizedAccessException or IOException)
        {
            LogNotListening(PipeName, ex.Message);
            return null;
        }
    }

    private void Track(Task instance)
    {
        lock (_instances)
        {
            _instances.RemoveAll(t => t.IsCompleted);
            _instances.Add(instance);
        }
    }

    private NamedPipeServerStream Create(bool firstInstance)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("The current Windows identity has no user SID.");
        var security = new PipeSecurity();

        // The owner is the user even when the Workbench runs elevated (else Administrators): the Host, which runs as the
        // user, checks it with CurrentUserOnly. A Host that runs elevated cannot use this pipe, nor the Bridge's.
        security.SetOwner(_owner ?? user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        PipeOptions options = PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options, 0, 0, security);
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        int client = PipeClient.ProcessId(pipe);
        if (client == 0 || !PipeClient.Runs(client, _expectedClient))
        {
            LogRefused(client);
            return;
        }

        try
        {
            await ConverseAsync(pipe, client).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            LogConnectionEnded(client, ex.Message); // what it means for the question was settled where it happened
        }
    }

    private async Task ConverseAsync(Stream pipe, int client)
    {
        // The handshake first: the Host's own process, a protocol both speak, and whether a question fits now.
        RpcMessage? hello = await ReadAsync(pipe, MessageTimeout).ConfigureAwait(false);
        if (hello is not { Id: long helloId, Method: WorkbenchProtocol.HandshakeMethod })
        {
            LogBroken(client, "no handshake first");
            if (hello is { Id: long id, Method: not null })
            {
                await SendAsync(pipe, WorkbenchJson.Error(id, WorkbenchProtocol.NotHandshakenErrorCode, "The handshake comes first.")).ConfigureAwait(false);
            }

            return;
        }

        WorkbenchHandshake handshake = WorkbenchJson.As<WorkbenchHandshake>(hello.Params);
        if (handshake.HostProcessId != client)
        {
            LogBroken(client, string.Create(CultureInfo.InvariantCulture, $"the handshake names process {handshake.HostProcessId}"));
            return;
        }

        int version = Math.Min(handshake.ProtocolVersion, WorkbenchProtocol.CurrentVersion);
        if (version < Math.Max(handshake.MinProtocolVersion, WorkbenchProtocol.MinimumSupportedVersion))
        {
            string speaks = string.Create(CultureInfo.InvariantCulture, $"This Workbench speaks protocol {WorkbenchProtocol.MinimumSupportedVersion} to {WorkbenchProtocol.CurrentVersion}.");
            await SendAsync(pipe, WorkbenchJson.Error(helloId, WorkbenchProtocol.UnsupportedVersionErrorCode, speaks)).ConfigureAwait(false);
            return;
        }

        bool accepting = !_quitting && _queue.TryReserve();
        await SendAsync(pipe, WorkbenchJson.Result(helloId, new WorkbenchHandshakeResult(version, _version, accepting))).ConfigureAwait(false);
        if (accepting)
        {
            await QuestionAsync(pipe, client).ConfigureAwait(false);
        }
    }

    /// <summary>The one question of a connection: read strictly into the queue, then answered there.</summary>
    private async Task QuestionAsync(Stream pipe, int client)
    {
        Question? question = null;
        bool reserved = true;
        try
        {
            if (await ReadAsync(pipe, MessageTimeout).ConfigureAwait(false) is not { Id: long askId } request || request.Method != WorkbenchProtocol.ApprovalAskMethod)
            {
                LogBroken(client, "no question after the handshake");
                return;
            }

            ApprovalAskParams? ask = null;
            try
            {
                ask = WorkbenchJson.As<ApprovalAskParams>(request.Params);
            }
            catch (WorkbenchProtocolException ex)
            {
                LogBroken(client, ex.Message);
            }

            if (ask is null || !Valid(ask))
            {
                await SendAsync(pipe, WorkbenchJson.Error(askId, InvalidParams, "The question is not valid.")).ConfigureAwait(false);
                return;
            }

            reserved = false; // Add takes the place, even when it refuses the question
            question = _queue.Add(ask);
            if (question is null)
            {
                LogDuplicate(ask.RequestId);
                await SendAsync(pipe, WorkbenchJson.Error(askId, InvalidRequest, "The same question is already waiting.")).ConfigureAwait(false);
                return;
            }

            LogAsked(question.RequestId, ask.Tool, client);
            await AnswerAsync(pipe, question, askId).ConfigureAwait(false);
        }
        finally
        {
            if (reserved)
            {
                _queue.Release();
            }

            // Whatever went wrong, no question stays waiting without its connection.
            if (question is not null && _queue.End(question, QuestionEnd.Disconnected))
            {
                LogEnded(question.RequestId, question.End);
            }
        }
    }

    /// <summary>What strict reading cannot check: the binding is there and the deadline is one a Host gives.</summary>
    private bool Valid(ApprovalAskParams ask) =>
        ask.RequestId.Length > 0 && ask.Tool.Length > 0 && ask.Fingerprint.Length > 0 && ask.ArgumentsHash.Length > 0 && ask.DeadlineUnixMs >= 0
        && ask.DeadlineUnixMs <= (_clock.GetUtcNow() + MaxDeadline).ToUnixTimeMilliseconds();

    /// <summary>Whether the message the Host sent while its question waited is the cancel of that question.</summary>
    private static bool IsCancel(Task<RpcMessage?> next, Question question)
    {
        try
        {
            return next.IsCompletedSuccessfully && next.Result is { Method: WorkbenchProtocol.CancelMethod, Id: null } cancel
                && WorkbenchJson.As<CancelParams>(cancel.Params).RequestId == question.RequestId;
        }
        catch (WorkbenchProtocolException)
        {
            return false; // a broken cancel is a broken connection
        }
    }

    private async Task AnswerAsync(Stream pipe, Question question, long askId)
    {
        // Reading goes on while the question waits: a cancel, or the end of the connection, ends it.
        Task<RpcMessage?> next = ReadAsync(pipe, Timeout.InfiniteTimeSpan);
        _ = next.ContinueWith(t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        TimeSpan left = question.Deadline - _clock.GetUtcNow();
        Task deadline = Task.Delay(left > TimeSpan.Zero ? left : TimeSpan.Zero, _clock, _stop.Token);
        Task first = await Task.WhenAny(question.Decision.Task, next, deadline).ConfigureAwait(false);
        if (first == next)
        {
            _queue.End(question, IsCancel(next, question) ? QuestionEnd.Cancelled : QuestionEnd.Disconnected);
            _queue.NoOutcome(question); // answered at that very moment, but the Host is gone
            LogEnded(question.RequestId, question.End);
            return;
        }

        if (first == deadline)
        {
            _queue.End(question, deadline.IsCompletedSuccessfully ? QuestionEnd.Expired : QuestionEnd.Closed);
        }

        WorkbenchDecision decision = await question.Decision.Task.ConfigureAwait(false);
        ApprovalAskParams ask = question.Ask;
        try
        {
            await SendAsync(pipe, WorkbenchJson.Result(askId, new ApprovalAskResult(ask.RequestId, ask.Fingerprint, ask.ArgumentsHash, decision))).ConfigureAwait(false);
            LogEnded(question.RequestId, question.End);
            if (question.Execution == QuestionExecution.Running)
            {
                await OutcomeAsync(pipe, question, next).ConfigureAwait(false);
            }
            else
            {
                await Task.WhenAny(next, Task.Delay(MessageTimeout, _clock, _stop.Token)).ConfigureAwait(false); // the Host hangs up
            }
        }
        finally
        {
            _queue.NoOutcome(question); // changes nothing when the outcome came
        }
    }

    /// <summary>The approved write's outcome, once, then the end of the connection; anything else breaks the protocol.</summary>
    private async Task OutcomeAsync(Stream pipe, Question question, Task<RpcMessage?> next)
    {
        RpcMessage? message = await next.ConfigureAwait(false);
        if (message is { Method: WorkbenchProtocol.ApprovalOutcomeMethod, Id: null }
            && WorkbenchJson.As<ApprovalOutcomeParams>(message.Params) is { } outcome && outcome.RequestId == question.RequestId)
        {
            _queue.Report(question, outcome.Execution, outcome.Reason);
            LogOutcome(question.RequestId, outcome.Execution, outcome.Reason);
            message = await ReadAsync(pipe, Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        }

        if (message is not null)
        {
            LogNotAnOutcome(question.RequestId);
        }
    }

    /// <summary>The next message, strictly read; null when the Host hung up between messages.</summary>
    private async Task<RpcMessage?> ReadAsync(Stream pipe, TimeSpan timeout)
    {
        using var limit = new CancellationTokenSource(timeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, limit.Token);
        byte[]? frame = await WorkbenchFraming.ReadAsync(pipe, linked.Token).ConfigureAwait(false);
        return frame is null ? null : WorkbenchJson.Read(frame);
    }

    private Task SendAsync(Stream pipe, byte[] message) => WorkbenchFraming.WriteAsync(pipe, message, _stop.Token);

    [LoggerMessage(Level = LogLevel.Information, Message = "Approval pipe {PipeName} is listening")]
    private partial void LogListening(string pipeName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Approval pipe {PipeName} is held by another program ({Problem}); this Workbench takes no questions")]
    private partial void LogNameTaken(string pipeName, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection from process {ProcessId} refused: it does not run the eplanmcp.exe beside this Workbench")]
    private partial void LogRefused(int processId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection from process {ProcessId} closed: {Problem}")]
    private partial void LogBroken(int processId, string problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection from process {ProcessId} ended: {Problem}")]
    private partial void LogConnectionEnded(int processId, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Question {RequestId} refused: the same question is already waiting")]
    private partial void LogDuplicate(string requestId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Question {RequestId} for {Tool} from process {ProcessId}")]
    private partial void LogAsked(string requestId, string tool, int processId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Question {RequestId} ended: {End}")]
    private partial void LogEnded(string requestId, QuestionEnd? end);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Question {RequestId}: the Host sent something other than one outcome; the connection is closed")]
    private partial void LogNotAnOutcome(string requestId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Approval pipe {PipeName} cannot take more connections ({Problem}); new questions go to the EPLAN dialog")]
    private partial void LogNotListening(string pipeName, string problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Question {RequestId}: the write reports {Execution} {Reason}")]
    private partial void LogOutcome(string requestId, WriteExecution execution, string? reason);
}
