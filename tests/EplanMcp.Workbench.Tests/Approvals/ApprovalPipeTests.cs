using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// The real Host channel (WorkbenchChannel) against the real Workbench pipe server, headless (ADR-0014, SEC-22, M5
/// design 7), on a pipe of this test's own, both sides expecting this test process as the other: a refused client, a
/// name already taken, the pipe's ACL, and what a broken or hostile client gets.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ApprovalPipeTests : IAsyncDisposable
{
    /// <summary>How long a step may take before the test fails instead of hanging.</summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly ManualTime _clock = new(Asks.Now);
    private readonly string _pipe = WorkbenchPipeName.For("S-1-5-21-0-0-0-1000", Path.Combine(Path.GetTempPath(), "eplanmcp-tests", Guid.NewGuid().ToString("N")));
    private readonly ApprovalServer _server;

    public ApprovalPipeTests()
    {
        _server = Server();
        Assert.True(_server.Start());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task TheHandshake_TakesNoQuestionYet_AndTheHostAsksElsewhere()
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();
        await SendAsync(pipe, WorkbenchJson.Request(1, WorkbenchProtocol.HandshakeMethod, new WorkbenchHandshake(1, 1, "test", Environment.ProcessId)));

        WorkbenchHandshakeResult result = WorkbenchJson.As<WorkbenchHandshakeResult>((await ReceiveAsync(pipe))!.Result);

        Assert.Equal((1, false), (result.ProtocolVersion, result.Accepting));
        Assert.Null(await Channel().AskAsync(HostQuestion(), Token));
    }

    [Fact]
    public async Task AClientThatDoesNotRunTheHostBesideTheWorkbench_IsRefusedBeforeAnything()
    {
        string pipe = _pipe + ".mismatch";
        await using ApprovalServer server = Server(pipe, expectedClient: Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"));
        Assert.True(server.Start());
        await using var raw = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await raw.ConnectAsync(TimeSpan.FromSeconds(5), Token);

        // The Workbench hangs up at once, so even sending the handshake may fail.
        await SendOrEndAsync(raw, WorkbenchJson.Request(1, WorkbenchProtocol.HandshakeMethod, new WorkbenchHandshake(1, 1, "test", Environment.ProcessId)));

        Assert.Null(await ReceiveOrEndAsync(raw));
        Assert.Null(await Channel(pipe).AskAsync(HostQuestion(), Token));
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!, "eplanmcp.exe"), ApprovalServer.ClientBeside(AppContext.BaseDirectory));
    }

    [Fact]
    public async Task ANameAlreadyTaken_ByAnotherWorkbenchOrProgram_IsReported_AndServesNothing()
    {
        await using ApprovalServer second = Server();
        string squatted = _pipe + ".squatted";
        using var squatter = new NamedPipeServerStream(squatted, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using ApprovalServer late = Server(squatted);

        Assert.False(second.Start());
        Assert.False(late.Start());
    }

    [Fact]
    public void ThePipe_AdmitsOnlyThisUser_AndNoNetworkLogon()
    {
        using var probe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        probe.Connect(TimeSpan.FromSeconds(5));
        PipeSecurity security = probe.GetAccessControl();
        using WindowsIdentity me = WindowsIdentity.GetCurrent();
        AuthorizationRule[] rules = [.. security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<AuthorizationRule>()];

        Assert.Equal(me.User, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(2, rules.Length);
        Assert.Contains(rules.OfType<PipeAccessRule>(), r => r.IdentityReference == me.User && r.AccessControlType == AccessControlType.Allow);
        Assert.Contains(rules.OfType<PipeAccessRule>(), r => r.IdentityReference == new SecurityIdentifier(WellKnownSidType.NetworkSid, null) && r.AccessControlType == AccessControlType.Deny);
    }

    [Fact]
    public async Task ARequestBeforeTheHandshake_IsTurnedAway()
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();

        await SendAsync(pipe, WorkbenchJson.Request(2, WorkbenchProtocol.ApprovalAskMethod, Asks.Write()));

        Assert.Equal(WorkbenchProtocol.NotHandshakenErrorCode, (await ReceiveAsync(pipe))!.Error!.Code);
        Assert.Null(await ReceiveOrEndAsync(pipe));
    }

    public static TheoryData<string> BrokenOpenings => new()
    {
        """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1,"hostVersion":"x","hostProcessId":1}}""",
        """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1,"hostVersion":"x","hostProcessId":0,"extra":true}}""",
        """{"jsonrpc":"2.0","method":"cancel","params":{"requestId":"r1"}}""",
        "not json",
    };

    /// <summary>A question only after a handshake that names this very process; anything else closes the connection.</summary>
    [Theory]
    [MemberData(nameof(BrokenOpenings))]
    public async Task ABrokenOpening_ClosesTheConnectionWithoutAnAnswer(string opening)
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();

        await SendAsync(pipe, Encoding.UTF8.GetBytes(opening.Replace("\"hostProcessId\":0", $"\"hostProcessId\":{Environment.ProcessId}", StringComparison.Ordinal)));

        Assert.Null(await ReceiveOrEndAsync(pipe));
    }

    [Fact]
    public async Task AnOversizedFrame_ClosesTheConnection()
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();

        await pipe.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {WorkbenchProtocol.MaxFrameBytes + 1}\r\n\r\n"), Token);

        Assert.Null(await ReceiveOrEndAsync(pipe));
    }

    [Fact]
    public async Task AHostOfAnotherProtocolVersion_IsToldWhichOnesThisWorkbenchSpeaks()
    {
        await using NamedPipeClientStream pipe = await ConnectAsync();

        await SendAsync(pipe, WorkbenchJson.Request(1, WorkbenchProtocol.HandshakeMethod, new WorkbenchHandshake(3, 2, "9.0.0", Environment.ProcessId)));

        Assert.Equal(WorkbenchProtocol.UnsupportedVersionErrorCode, (await ReceiveAsync(pipe))!.Error!.Code);
        Assert.Null(await ReceiveOrEndAsync(pipe));
    }

    private ApprovalServer Server(string? pipe = null, string? expectedClient = null) =>
        new(pipe ?? _pipe, expectedClient ?? Environment.ProcessPath!, _clock, NullLogger.Instance);

    /// <summary>The Host's channel on this test's pipe (or <paramref name="pipe"/>), expecting this test process as the Workbench.</summary>
    private WorkbenchChannel Channel(string? pipe = null) =>
        new(pipe ?? _pipe, Environment.ProcessPath!, () => "claude-code", _clock, NullLogger.Instance);

    private ApprovalQuestion HostQuestion(string requestId = "r1")
    {
        var plan = new WritePlan
        {
            Project = "Line1",
            ProjectPath = @"D:\Projects\Line1",
            Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }],
            Fingerprint = "f1",
        };
        var eplan = new EplanInstanceInfo { ProcessId = 4242, Version = "2026.0.3.25421", Variant = "Pro Panel" };
        return new ApprovalQuestion(requestId, "eplan_page_create", "h1", plan, ["create page =A1&EFS1/5"], _clock.GetUtcNow() + TimeSpan.FromSeconds(120))
        {
            Tier = ToolTier.Write,
            Eplan = eplan,
        };
    }

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(5), Token);
        return pipe;
    }

    private static Task SendAsync(Stream pipe, byte[] message) => WorkbenchFraming.WriteAsync(pipe, message, Token);

    private static async Task SendOrEndAsync(Stream pipe, byte[] message)
    {
        try
        {
            await SendAsync(pipe, message);
        }
        catch (IOException)
        {
            // the Workbench closed the connection first
        }
    }

    private static async Task<RpcMessage?> ReceiveAsync(Stream pipe) =>
        await WorkbenchFraming.ReadAsync(pipe, Token).WaitAsync(_patience, Token) is { } frame ? WorkbenchJson.Read(frame) : null;

    /// <summary>The next message; null when the Workbench closed the connection instead.</summary>
    private static async Task<RpcMessage?> ReceiveOrEndAsync(Stream pipe)
    {
        try
        {
            return await ReceiveAsync(pipe);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
