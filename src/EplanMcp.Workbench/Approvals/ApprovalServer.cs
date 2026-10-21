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
/// The Workbench's end of the approval pipe (ADR-0014 decisions 2 and 3, SEC-22). It takes the pipe name of this user
/// and data root as its first instance, or serves nothing; the ACL admits the current user only and no network logon.
/// Each connection is one Host: its process must run <c>eplanmcp.exe</c> beside the Workbench (a guard against
/// mismatches, not authentication), the handshake comes first and every message is read strictly. It takes no question
/// yet: the handshake says so, and the Host asks in the EPLAN dialog.
/// </summary>
internal sealed partial class ApprovalServer : IAsyncDisposable
{
    /// <summary>How long a Host may take for its handshake, then for its question, and to hang up after the answer.</summary>
    public static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(5);

    private static readonly string _version = typeof(ApprovalServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    private readonly string _expectedClient;
    private readonly SecurityIdentifier? _owner;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _instances = [];
    private int _disposed;

    /// <summary>The Workbench of this user and <paramref name="dataRoot"/>, serving the Host installed beside it.</summary>
    public ApprovalServer(string dataRoot, TimeProvider clock, ILogger logger)
        : this(WorkbenchPipeName.ForCurrentUser(dataRoot), ClientBeside(AppContext.BaseDirectory), clock, logger)
    {
    }

    /// <summary>
    /// For tests: their own pipe and client image, never from configuration, and the pipe's owner. A test that runs
    /// elevated, as on CI, gives its token's owner (Administrators), which the Host's <c>CurrentUserOnly</c> compares with;
    /// the Workbench itself always makes the user the owner.
    /// </summary>
    internal ApprovalServer(string pipeName, string expectedClient, TimeProvider clock, ILogger logger, SecurityIdentifier? owner = null)
    {
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

    /// <summary>Ends every connection.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
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
        // The handshake first: the Host's own process, and a protocol both speak.
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

        await SendAsync(pipe, WorkbenchJson.Result(helloId, new WorkbenchHandshakeResult(version, _version, Accepting: false))).ConfigureAwait(false);
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



    [LoggerMessage(Level = LogLevel.Error, Message = "Approval pipe {PipeName} cannot take more connections ({Problem}); new questions go to the EPLAN dialog")]
    private partial void LogNotListening(string pipeName, string problem);
}
