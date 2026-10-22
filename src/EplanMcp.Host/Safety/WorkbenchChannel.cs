using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Workbench.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Safety;

/// <summary>
/// Asks the engineer in the EplanMcp Workbench (ADR-0014, M5 design 4.6), tried before the EPLAN dialog. For each
/// question it connects once to the Workbench's pipe of this user and data root, checks that the server runs the
/// Workbench beside this Host (a guard against a mismatched build or a squatted name, not authentication), agrees on a
/// protocol version and sends approval.ask. Until the question is sent whole it can step aside (null) and the EPLAN
/// dialog asks. Once it is, only the Workbench answers it: a broken connection or message closes the question,
/// unanswered, and it is never asked in EPLAN as well. The wait lasts until the deadline and <see cref="Slack"/>, and
/// holds nothing of the Host's queue to EPLAN; <see cref="ApprovalService"/> judges whether an answer counts.
/// </summary>
internal sealed partial class WorkbenchChannel : IApprovalChannel
{
    public const string ChannelName = "workbench";

    /// <summary>How long a connection may take: a Workbench that is not running costs every approval this much.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Beyond the deadline, for the Workbench's own "expired" to arrive.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(15);

    /// <summary>How long the handshake's answer, and the cancel notice, may take.</summary>
    public static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2);

    private const long HandshakeId = 1;
    private const long AskId = 2;
    private const uint QueryLimitedInformation = 0x1000;

    private readonly string _pipeName;
    private readonly string _workbench;
    private readonly Func<string?> _clientName;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    /// <summary>The Workbench of this user and data root, expected at <c>workbench\EplanMcp.Workbench.exe</c> beside the Host.</summary>
    public WorkbenchChannel(HostDataLocations locations, IServiceProvider services, TimeProvider clock, ILogger<WorkbenchChannel> logger)
        : this(
            WorkbenchPipeName.ForCurrentUser(locations.Root),
            Path.Combine(AppContext.BaseDirectory, "workbench", "EplanMcp.Workbench.exe"),
            () => services.GetService<McpServer>()?.ClientInfo?.Name, // only under a transport, once the client said hello
            clock,
            logger)
    {
    }

    /// <summary>For tests: their own pipe and the image they serve it from; never from configuration.</summary>
    internal WorkbenchChannel(string pipeName, string workbench, Func<string?> clientName, TimeProvider clock, ILogger logger)
    {
        _pipeName = pipeName;
        _workbench = workbench;
        _clientName = clientName;
        _clock = clock;
        _logger = logger;
    }

    public string Name => ChannelName;

    public string Where => "in the EplanMcp Workbench";

    public async Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
    {
        QuestionTier? tier = question.Tier switch { ToolTier.Write => QuestionTier.Write, ToolTier.Destructive => QuestionTier.Destructive, _ => null };
        // Version 1 has no field for system master data (ADR-0019), so EPLAN's dialog asks about those writes and their
        // suspensions with the database and "every project is affected" in its text; nor for installing a pack (ADR-0024).
        if (tier is null || question.Eplan is not { } eplan || question.Plan.MasterData is not null || question.Release?.Database is not null || question.PackInstall is not null)
        {
            return null; // version 1 cannot show it
        }

        NamedPipeClientStream? pipe = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (pipe is null || !await AgreeAsync(pipe, cancellationToken).ConfigureAwait(false) || Ask(question, tier.Value, eplan) is not { } ask
                || await DeliverAsync(pipe, question, ask, cancellationToken).ConfigureAwait(false) is not { } answer)
            {
                return null;
            }

            if (answer.Decision != ApprovalDecision.Granted)
            {
                return answer;
            }

            // A grant keeps the connection open, for the write's outcome to be shown on it (approval.outcome).
            var outcome = new Outcome(pipe, question.RequestId, _clock, _logger);
            pipe = null;
            return answer with { Outcome = outcome };
        }
        finally
        {
            pipe?.Dispose();
        }
    }

    /// <summary>
    /// Sends the question and waits for its answer; null when it could not be sent whole, so the Workbench never showed
    /// it. From then on, only the Workbench answers it.
    /// </summary>
    internal async Task<ApprovalAnswer?> DeliverAsync(Stream pipe, ApprovalQuestion question, byte[] ask, CancellationToken cancellationToken)
    {
        TimeSpan wait = question.Deadline + Slack - _clock.GetUtcNow();
        using var deadline = new CancellationTokenSource(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _clock);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        bool sent = false;
        try
        {
            await WorkbenchFraming.WriteAsync(pipe, ask, waiting.Token).ConfigureAwait(false);
            sent = true;
            RpcMessage response = await ResponseAsync(pipe, AskId, waiting.Token).ConfigureAwait(false);
            if (response.Error is { } error)
            {
                LogClosed(question.RequestId, error.Message);
                return Unanswered(question, ApprovalDecision.Cancelled);
            }

            ApprovalAskResult result = WorkbenchJson.As<ApprovalAskResult>(response.Result);
            return new ApprovalAnswer(result.RequestId, result.Fingerprint, result.ArgumentsHash, result.Decision switch
            {
                WorkbenchDecision.Approved => ApprovalDecision.Granted,
                WorkbenchDecision.Rejected => ApprovalDecision.Denied,
                WorkbenchDecision.Expired => ApprovalDecision.Expired,
                _ => ApprovalDecision.Cancelled,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CancelAsync(pipe, question.RequestId).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            return Unanswered(question, ApprovalDecision.Expired);
        }
        catch (IOException ex) when (!sent)
        {
            LogUnreachable(ex.Message);
            return null;
        }
        catch (IOException ex)
        {
            LogClosed(question.RequestId, ex.Message);
            return Unanswered(question, ApprovalDecision.Cancelled);
        }
    }

    /// <summary>The question as an approval.ask request; null, logged, when this protocol version cannot carry it.</summary>
    private byte[]? Ask(ApprovalQuestion question, QuestionTier tier, EplanInstanceInfo eplan)
    {
        WritePlan plan = question.Plan;
        Suspension? suspension = question.Release;
        byte[] ask;
        try
        {
            ask = WorkbenchJson.Request(AskId, WorkbenchProtocol.ApprovalAskMethod, new ApprovalAskParams(
                question.RequestId,
                suspension is null ? QuestionKind.Approval : QuestionKind.SuspensionRelease,
                question.Tool,
                tier,
                new ProjectInfo(plan.Project, plan.ProjectPath),
                new EplanInstance(eplan.Version, eplan.Variant, eplan.ProcessId),
                _clientName(),
                ApprovalTexts.For(question, CultureInfo.CurrentUICulture).Changes,
                question.BackupFolder,
                suspension is null ? null : new SuspensionRecord(
                    suspension.RequestId,
                    suspension.Tool,
                    suspension.Time == DateTimeOffset.MinValue ? null : suspension.Time.ToUnixTimeMilliseconds(),
                    suspension.Project,
                    suspension.ProcessId),
                plan.Fingerprint,
                question.ArgumentsHash,
                question.Deadline.ToUnixTimeMilliseconds()));
        }
        catch (JsonException ex)
        {
            LogNotAsked(ex.Message); // a null where version 1 allows none
            return null;
        }

        if (ask.Length > WorkbenchProtocol.MaxFrameBytes)
        {
            LogNotAsked($"{ask.Length.ToString(CultureInfo.InvariantCulture)} bytes, over the frame limit");
            return null;
        }

        return ask;
    }

    /// <summary>
    /// The connected pipe, served by this user (<see cref="PipeOptions.CurrentUserOnly"/>) from the expected image; null
    /// when nothing listens, every instance is busy, another user owns the name, or another program serves it.
    /// </summary>
    private async Task<NamedPipeClientStream?> ConnectAsync(CancellationToken cancellationToken)
    {
        NamedPipeClientStream? pipe = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
            string? image = ServerImage(pipe);
            if (image is null || !string.Equals(Canonical(image), Canonical(_workbench), StringComparison.OrdinalIgnoreCase))
            {
                LogNotTheWorkbench(image ?? "unknown");
                return null;
            }

            NamedPipeClientStream connected = pipe;
            pipe = null; // the caller's now
            return connected;
        }
        catch (TimeoutException)
        {
            LogNotRunning();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUnreachable(ex.Message);
            return null;
        }
        finally
        {
            pipe?.Dispose();
        }
    }

    /// <summary>Whether the Workbench speaks a version this Host speaks and takes a new question; nothing is asked yet.</summary>
    private async Task<bool> AgreeAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        var hello = new WorkbenchHandshake(WorkbenchProtocol.CurrentVersion, WorkbenchProtocol.MinimumSupportedVersion, HostInfo.Version, Environment.ProcessId);
        using var timeout = new CancellationTokenSource(ReplyTimeout, _clock);
        using var reply = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            await WorkbenchFraming.WriteAsync(pipe, WorkbenchJson.Request(HandshakeId, WorkbenchProtocol.HandshakeMethod, hello), reply.Token).ConfigureAwait(false);
            RpcMessage answer = await ResponseAsync(pipe, HandshakeId, reply.Token).ConfigureAwait(false);
            WorkbenchHandshakeResult? result = answer.Error is null ? WorkbenchJson.As<WorkbenchHandshakeResult>(answer.Result) : null;
            if (result is null || result.ProtocolVersion is < WorkbenchProtocol.MinimumSupportedVersion or > WorkbenchProtocol.CurrentVersion)
            {
                LogIncompatible(result?.ProtocolVersion, answer.Error?.Message);
                return false;
            }

            if (!result.Accepting)
            {
                LogNotAccepting();
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnreachable("no answer to the handshake");
            return false;
        }
        catch (IOException ex)
        {
            LogUnreachable(ex.Message);
            return false;
        }
    }

    /// <summary>Tells the Workbench to drop the question the client no longer waits for; the connection closes anyway.</summary>
    private async Task CancelAsync(Stream pipe, string requestId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(ReplyTimeout, _clock);
            await WorkbenchFraming.WriteAsync(pipe, WorkbenchJson.Notification(WorkbenchProtocol.CancelMethod, new CancelParams(requestId)), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            LogClosed(requestId, "cancel not sent: " + ex.Message);
        }
    }

    /// <summary>The answer to request <paramref name="id"/>; anything else on the pipe breaks the protocol.</summary>
    private static async Task<RpcMessage> ResponseAsync(Stream pipe, long id, CancellationToken cancellationToken)
    {
        byte[] frame = await WorkbenchFraming.ReadAsync(pipe, cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("The Workbench closed the connection.");
        RpcMessage message = WorkbenchJson.Read(frame);
        return message.Method is null && message.Id == id ? message : throw new WorkbenchProtocolException("The Workbench sent something other than the answer.");
    }

    /// <summary>The question's own binding with <paramref name="decision"/>: what the Host decides when no answer came.</summary>
    private static ApprovalAnswer Unanswered(ApprovalQuestion question, ApprovalDecision decision) =>
        new(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, decision);

    /// <summary>
    /// The image of the process serving the pipe; null when it cannot be read. Limited query access suffices, so an
    /// elevated Workbench is read too.
    /// </summary>
    private static string? ServerImage(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint processId))
        {
            return null;
        }

        using SafeProcessHandle process = OpenProcess(QueryLimitedInformation, inheritHandle: false, processId);
        char[] image = new char[32_768];
        int length = image.Length;
        return !process.IsInvalid && QueryFullProcessImageName(process, 0, image, ref length) ? new string(image, 0, length) : null;
    }

    /// <summary>Both sides of the image comparison as the file system resolves them: short names and links followed.</summary>
    private static string Canonical(string path)
    {
        try
        {
            return ScratchPaths.Canonical(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Path.GetFullPath(path);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] image, ref int length);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Workbench not running; the next approval channel asks")]
    private partial void LogNotRunning();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workbench pipe served by {Image}, not by the Workbench beside this Host; the next approval channel asks")]
    private partial void LogNotTheWorkbench(string image);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workbench cannot be asked ({Problem}); the next approval channel asks")]
    private partial void LogUnreachable(string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workbench speaks protocol {Version} ({Error}); the next approval channel asks")]
    private partial void LogIncompatible(int? version, string? error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Workbench takes no new question now; the next approval channel asks")]
    private partial void LogNotAccepting();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workbench cannot be sent this question ({Problem}); the next approval channel asks")]
    private partial void LogNotAsked(string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workbench question {RequestId} closed unanswered: {Problem}")]
    private partial void LogClosed(string requestId, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workbench not shown the outcome of {RequestId}: {Problem}")]
    private static partial void LogOutcomeNotShown(ILogger logger, string requestId, string problem);

    /// <summary>The granted question's connection: shows the write's outcome on it, then closes it.</summary>
    private sealed class Outcome(NamedPipeClientStream pipe, string requestId, TimeProvider clock, ILogger logger) : IApprovalOutcome
    {
        public async Task ReportAsync(string execution, string? reason)
        {
            WriteExecution state = execution switch
            {
                ExecutionState.NotStarted => WriteExecution.NotStarted,
                ExecutionState.RolledBack => WriteExecution.RolledBack,
                ExecutionState.Committed => WriteExecution.Committed,
                _ => WriteExecution.Unknown,
            };
            try
            {
                using var timeout = new CancellationTokenSource(ReplyTimeout, clock);
                byte[] notification = WorkbenchJson.Notification(WorkbenchProtocol.ApprovalOutcomeMethod, new ApprovalOutcomeParams(requestId, state, reason));
                await WorkbenchFraming.WriteAsync(pipe, notification, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException)
            {
                LogOutcomeNotShown(logger, requestId, ex.Message); // gone, broken or closed: the write stands as it is
            }
        }

        public void Dispose() => pipe.Dispose();
    }
}
