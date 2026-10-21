using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Bridge;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Workbench.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// The Workbench channel (ADR-0014, M5 design 4.6) against a fake Workbench on a real pipe of this test's own: when it
/// steps aside so that the EPLAN dialog asks, what it sends, how each answer maps, and that once a question is sent
/// only the Workbench answers it, with ApprovalService refusing answers that are late or belong to another question.
/// </summary>
[Trait("Size", "Medium")]
public sealed class WorkbenchChannelTests : IAsyncDisposable
{
    private static readonly TimeSpan _timeout = HostConfig.DefaultApprovalTimeout;

    /// <summary>How long the fake Workbench may take to see something happen; a failing test fails rather than hangs.</summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(5);

    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-workbench-").FullName);
    private readonly ManualTime _clock = new(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeWorkbench _workbench;
    private readonly WritePlan _plan = new()
    {
        Project = "Line1",
        ProjectPath = @"D:\Projects\Line1",
        Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }],
        Fingerprint = "f1",
    };

    public WorkbenchChannelTests() => _workbench = new FakeWorkbench(WorkbenchPipeName.ForCurrentUser(_locations.Root));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _workbench.DisposeAsync();
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Fact]
    public async Task NotRunning_StepsAside()
    {
        var elsewhere = new HostDataLocations(Path.Combine(_locations.Root, "other"));

        Assert.Null(await Channel(WorkbenchPipeName.ForCurrentUser(elsewhere.Root)).AskAsync(Question(), Token));
    }

    [Fact]
    public async Task EveryInstanceBusy_TheConnectTimesOutAndItStepsAside()
    {
        await using var busy = new FakeWorkbench(WorkbenchPipeName.ForCurrentUser(Path.Combine(_locations.Root, "busy")), instances: 1);
        using var other = new NamedPipeClientStream(".", busy.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await other.ConnectAsync(TimeSpan.FromSeconds(5), Token);
        await busy.ConnectedAsync(1, Token);

        Assert.Null(await Channel(busy.PipeName).AskAsync(Question(), Token));
        Assert.Empty(busy.Handshakes);
    }

    [Fact]
    public async Task ThePipeOfThisUserAndDataRoot_ServedByAnyProgramButTheWorkbenchBesideTheHost_SaysNothingAndStepsAside()
    {
        var production = new WorkbenchChannel(_locations, new ServiceProviderStub(), _clock, NullLogger<WorkbenchChannel>.Instance);
        var elsewhere = Channel(image: Path.Combine(_locations.Root, "EplanMcp.Workbench.exe"));

        Assert.Null(await production.AskAsync(Question(), Token));
        Assert.Null(await elsewhere.AskAsync(Question(), Token));
        await _workbench.ConnectedAsync(2, Token);
        Assert.Empty(_workbench.Handshakes);
    }

    [Fact]
    public async Task AnIncompatibleProtocol_StepsAsideBeforeAsking()
    {
        _workbench.Welcome = new WorkbenchHandshakeResult(WorkbenchProtocol.CurrentVersion + 1, "2.0.0", Accepting: true);
        ApprovalAnswer? newer = await Channel().AskAsync(Question(), Token);
        _workbench.HandshakeReply = id => WorkbenchJson.Error(id, WorkbenchProtocol.UnsupportedVersionErrorCode, "version 2 only");
        ApprovalAnswer? refused = await Channel().AskAsync(Question(), Token);
        _workbench.HandshakeReply = id => Encoding.UTF8.GetBytes($$$"""{"jsonrpc":"2.0","id":{{{id}}},"result":{"protocolVersion":1,"workbenchVersion":"x","accepting":true,"more":1}}""");
        ApprovalAnswer? garbled = await Channel().AskAsync(Question(), Token);

        Assert.All([newer, refused, garbled], Assert.Null);
        Assert.Equal(3, _workbench.Handshakes.Count);
        Assert.Empty(_workbench.Asks);
    }

    [Fact]
    public async Task AWorkbenchThatTakesNoNewQuestion_StepsAside()
    {
        _workbench.Welcome = new WorkbenchHandshakeResult(WorkbenchProtocol.CurrentVersion, "test", Accepting: false);

        Assert.Null(await Channel().AskAsync(Question(), Token));
        Assert.Empty(_workbench.Asks);
    }

    [Fact]
    public async Task NoAnswerToTheHandshake_StepsAsideAfterTheReplyTimeout()
    {
        _workbench.HandshakeReply = _ => null;

        Task<ApprovalAnswer?> asking = Channel().AskAsync(Question(), Token);
        await _workbench.Greeted.Task.WaitAsync(_patience, Token);
        _clock.Advance(WorkbenchChannel.ReplyTimeout);

        Assert.Null(await asking);
        Assert.Empty(_workbench.Asks);
    }

    [Fact]
    public async Task TheHandshakeAndTheQuestion_CarryWhatTheWorkbenchShows()
    {
        ApprovalQuestion question = Question();

        await Channel().AskAsync(question, Token);

        WorkbenchHandshake hello = Assert.Single(_workbench.Handshakes);
        Assert.Equal((1, 1, HostInfo.Version, Environment.ProcessId), (hello.ProtocolVersion, hello.MinProtocolVersion, hello.HostVersion, hello.HostProcessId));
        ApprovalAskParams ask = Assert.Single(_workbench.Asks);
        Assert.Equal(("r1", QuestionKind.Approval, "eplan_page_create", QuestionTier.Write), (ask.RequestId, ask.Kind, ask.Tool, ask.Tier));
        Assert.Equal(new ProjectInfo("Line1", @"D:\Projects\Line1"), ask.Project);
        Assert.Equal(new EplanInstance("2026.0.3.25421", "Pro Panel", 4242), ask.Eplan);
        Assert.Equal("claude-code", ask.ClientName);
        Assert.Equal(ApprovalTexts.For(question, CultureInfo.CurrentUICulture).Changes, ask.Changes);
        Assert.Equal((null, null), (ask.BackupFolder, ask.Suspension));
        Assert.Equal(("f1", "h1", (_clock.Now + _timeout).ToUnixTimeMilliseconds()), (ask.Fingerprint, ask.ArgumentsHash, ask.DeadlineUnixMs));
    }

    [Fact]
    public async Task ADestructiveWriteAndAReleaseQuestion_CarryTheBackupAndTheSuspension()
    {
        var suspension = new Suspension(null, 4242, null, "eplan_page_delete", "earlier", _clock.Now - TimeSpan.FromHours(1), SuspensionReason.OutcomeUnknown, ["delete page =A1&EFS1/5"]);

        await Channel().AskAsync(Question() with { BackupFolder = @"D:\EplanMcp\backups\Line1", Tier = ToolTier.Destructive }, Token);
        await Channel().AskAsync(Question() with { Release = suspension }, Token);

        ApprovalAskParams[] asks = [.. _workbench.Asks];
        Assert.Equal((QuestionTier.Destructive, @"D:\EplanMcp\backups\Line1"), (asks[0].Tier, asks[0].BackupFolder));
        Assert.Equal(QuestionKind.SuspensionRelease, asks[1].Kind);
        Assert.Equal(new SuspensionRecord("earlier", "eplan_page_delete", suspension.Time.ToUnixTimeMilliseconds(), null, 4242), asks[1].Suspension);
        Assert.Equal(["delete page =A1&EFS1/5"], asks[1].Changes);
        Assert.Equal(2, _workbench.Connections);
    }

    [Fact]
    public async Task AToolTierTheProtocolCannotName_OrAQuestionWithoutItsEplan_StepsAside()
    {
        Assert.Null(await Channel().AskAsync(Question() with { Tier = ToolTier.Code }, Token));
        Assert.Null(await Channel().AskAsync(Question() with { Eplan = null }, Token));
        Assert.Equal(0, _workbench.Connections);
    }

    [Fact]
    public async Task AQuestionVersion1CannotCarry_IsNotSentAndTheEplanDialogAsks()
    {
        var dialog = new FakeApprovalChannel(EplanDialogChannel.ChannelName, ApprovalDecision.Granted);
        EplanInstanceInfo eplan = Eplan();
        eplan.Version = null!; // as from an add-in that sent none

        ApprovalResult result = await Service(Channel(), dialog).AskAsync("r1", "eplan_page_create", ToolTier.Write, "h1", _plan, eplan, null, null, Token);

        Assert.Equal(new ApprovalResult(EplanDialogChannel.ChannelName, ApprovalDecision.Granted), result);
        Assert.Single(_workbench.Handshakes);
        Assert.Empty(_workbench.Asks);
    }

    [Fact]
    public async Task ASuspensionOfUnknownTime_IsSentWithoutOne()
    {
        var suspension = new Suspension(@"D:\Projects\Line1", null, null, "eplan_page_create", "earlier", DateTimeOffset.MinValue, SuspensionReason.OutcomeUnknown, []);

        await Channel().AskAsync(Question() with { Release = suspension }, Token);

        Assert.Null(Assert.Single(_workbench.Asks).Suspension!.TimeUnixMs);
    }

    [Theory]
    [InlineData(WorkbenchDecision.Approved, nameof(ApprovalDecision.Granted))]
    [InlineData(WorkbenchDecision.Rejected, nameof(ApprovalDecision.Denied))]
    [InlineData(WorkbenchDecision.Closed, nameof(ApprovalDecision.Cancelled))]
    [InlineData(WorkbenchDecision.Expired, nameof(ApprovalDecision.Expired))]
    public async Task TheWorkbenchsDecision_IsMapped(WorkbenchDecision answered, string decision)
    {
        _workbench.Answer = FakeWorkbench.Decide(answered);

        ApprovalAnswer? answer = await Channel().AskAsync(Question(), Token);

        Assert.Equal(new ApprovalAnswer("r1", "f1", "h1", Enum.Parse<ApprovalDecision>(decision)), answer);
        await _workbench.HungUp.Task.WaitAsync(_patience, Token);
    }

    [Fact]
    public async Task NoAnswerByTheDeadlineAndTheSlack_HasExpiredAndTheConnectionCloses()
    {
        _workbench.Answer = _silent;

        Task<ApprovalAnswer?> asking = Channel().AskAsync(Question(), Token);
        await _workbench.Asked.Task.WaitAsync(_patience, Token);
        _clock.Advance(_timeout + WorkbenchChannel.Slack - TimeSpan.FromSeconds(1));
        Assert.False(asking.IsCompleted);
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(new ApprovalAnswer("r1", "f1", "h1", ApprovalDecision.Expired), await asking);
        await _workbench.HungUp.Task.WaitAsync(_patience, Token);
    }

    [Fact]
    public async Task AWorkbenchThatHangsUpAfterTheQuestion_ClosesItAndTheEplanDialogIsNotAsked()
    {
        _workbench.Answer = (_, _, _) => Task.FromResult<IReadOnlyList<byte[]>?>(null);
        var dialog = new FakeApprovalChannel(EplanDialogChannel.ChannelName, ApprovalDecision.Granted);

        ApprovalResult result = await Service(Channel(), dialog).AskAsync("r1", "eplan_page_create", ToolTier.Write, "h1", _plan, Eplan(), null, null, Token);

        Assert.Equal(new ApprovalResult(WorkbenchChannel.ChannelName, ApprovalDecision.Cancelled), result);
        Assert.Empty(dialog.Questions);
    }

    public static TheoryData<string> BrokenAnswers => new()
    {
        """{"jsonrpc":"2.0","id":2,"result":{"requestId":"r1","fingerprint":"f1","argumentsHash":"h1","decision":"approved","note":"pre-approved"}}""",
        """{"jsonrpc":"2.0","id":2,"result":{"requestId":"r1","fingerprint":"f1","argumentsHash":"h1","decision":"approved","decision":"approved"}}""",
        """{"jsonrpc":"2.0","id":2,"result":{"requestId":"r1","fingerprint":"f1","argumentsHash":"h1","decision":"Approved"}}""",
        """{"jsonrpc":"2.0","id":1,"result":{"requestId":"r1","fingerprint":"f1","argumentsHash":"h1","decision":"approved"}}""",
        """{"jsonrpc":"2.0","method":"approval.outcome","params":{"requestId":"r1","execution":"committed","reason":null}}""",
        """{"jsonrpc":"2.0","id":2,"error":{"code":-32603,"message":"internal"}}""",
    };

    [Theory]
    [MemberData(nameof(BrokenAnswers))]
    public async Task ABrokenAnswer_ClosesTheQuestionAndTheEplanDialogIsNotAsked(string answer)
    {
        _workbench.Answer = (_, _, _) => Task.FromResult<IReadOnlyList<byte[]>?>([Encoding.UTF8.GetBytes(answer)]);
        var dialog = new FakeApprovalChannel(EplanDialogChannel.ChannelName, ApprovalDecision.Granted);

        ApprovalResult result = await Service(Channel(), dialog).AskAsync("r1", "eplan_page_create", ToolTier.Write, "h1", _plan, Eplan(), null, null, Token);

        Assert.Equal(ApprovalDecision.Cancelled, result.Decision);
        Assert.Empty(dialog.Questions);
    }

    [Fact]
    public async Task AnOversizedAnswer_ClosesTheQuestion()
    {
        _workbench.Answer = (_, _, _) => Task.FromResult<IReadOnlyList<byte[]>?>([Encoding.ASCII.GetBytes($"Content-Length: {WorkbenchProtocol.MaxFrameBytes + 1}\r\n\r\n")]);

        ApprovalAnswer? answer = await Channel().AskAsync(Question(), Token);

        Assert.Equal(ApprovalDecision.Cancelled, answer!.Decision);
    }

    [Fact]
    public async Task AnApprovalAfterTheDeadline_HasExpired()
    {
        var late = new TaskCompletionSource();
        _workbench.Answer = async (ask, id, stop) =>
        {
            await late.Task.WaitAsync(stop);
            return await FakeWorkbench.Decide(WorkbenchDecision.Approved)(ask, id, stop);
        };

        Task<ApprovalResult> asking = Service(Channel()).AskAsync("r1", "eplan_page_create", ToolTier.Write, "h1", _plan, Eplan(), null, null, Token);
        await _workbench.Asked.Task.WaitAsync(_patience, Token);
        _clock.Advance(_timeout + TimeSpan.FromSeconds(1));
        late.SetResult();

        Assert.Equal(new ApprovalResult(WorkbenchChannel.ChannelName, ApprovalDecision.Expired), await asking);
    }

    [Theory]
    [InlineData("earlier", "f1", "h1")]
    [InlineData("r1", "f0", "h1")]
    [InlineData("r1", "f1", "h0")]
    public async Task AnApprovalForAnotherQuestion_IsDenied(string requestId, string fingerprint, string argumentsHash)
    {
        _workbench.Answer = (_, id, _) => Task.FromResult<IReadOnlyList<byte[]>?>([WorkbenchJson.Result(id, new ApprovalAskResult(requestId, fingerprint, argumentsHash, WorkbenchDecision.Approved))]);

        ApprovalResult result = await Service(Channel()).AskAsync("r1", "eplan_page_create", ToolTier.Write, "h1", _plan, Eplan(), null, null, Token);

        Assert.Equal(ApprovalDecision.Denied, result.Decision);
    }

    [Fact]
    public async Task ASecondAnswerToOneQuestion_IsNeverRead()
    {
        _workbench.Answer = (ask, id, _) => Task.FromResult<IReadOnlyList<byte[]>?>(
        [
            WorkbenchJson.Result(id, new ApprovalAskResult(ask.RequestId, ask.Fingerprint, ask.ArgumentsHash, WorkbenchDecision.Rejected)),
            WorkbenchJson.Result(id, new ApprovalAskResult(ask.RequestId, ask.Fingerprint, ask.ArgumentsHash, WorkbenchDecision.Approved)),
        ]);

        ApprovalAnswer? first = await Channel().AskAsync(Question(), Token);
        ApprovalAnswer? next = await Channel().AskAsync(Question(), Token);

        Assert.Equal(ApprovalDecision.Denied, first!.Decision);
        Assert.Equal(ApprovalDecision.Denied, next!.Decision);
        Assert.Equal(2, _workbench.Connections);
    }

    [Fact]
    public async Task WhenTheClientCancels_TheWorkbenchIsToldToDropTheQuestion()
    {
        _workbench.Answer = _silent;
        using var client = CancellationTokenSource.CreateLinkedTokenSource(Token);

        Task<ApprovalAnswer?> asking = Channel().AskAsync(Question(), client.Token);
        await _workbench.Asked.Task.WaitAsync(_patience, Token);
        await client.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
        await _workbench.HungUp.Task.WaitAsync(_patience, Token);
        (string method, JsonElement? parameters) = Assert.Single(_workbench.Notifications);
        Assert.Equal((WorkbenchProtocol.CancelMethod, new CancelParams("r1")), (method, WorkbenchJson.As<CancelParams>(parameters)));
    }

    [Fact]
    public async Task WhenTheWorkbenchStepsAside_TheEplanDialogAsks()
    {
        var dialog = new FakeApprovalChannel(EplanDialogChannel.ChannelName, ApprovalDecision.Granted);
        WorkbenchChannel absent = Channel(WorkbenchPipeName.ForCurrentUser(Path.Combine(_locations.Root, "other")));

        ApprovalResult result = await Service(absent, dialog).AskAsync("r1", "eplan_page_create", ToolTier.Write, "h1", _plan, Eplan(), null, null, Token);

        Assert.Equal(new ApprovalResult(EplanDialogChannel.ChannelName, ApprovalDecision.Granted), result);
        Assert.Single(dialog.Questions);
    }

    [Fact]
    public async Task WhileTheWorkbenchWaits_EplanStaysFreeForOtherCalls()
    {
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(new BridgeEndpoint("EplanMcp.Bridge.42.a1", 42));
        await using var bridge = new BridgeClient(discovery, new FakeConnector(endpoint => new FakeChannel(endpoint)), NullLogger<BridgeClient>.Instance, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5));
        ProjectListData? meanwhile = null;
        _workbench.Answer = async (ask, id, stop) =>
        {
            meanwhile = await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new ProjectListArgs(), stop);
            return await FakeWorkbench.Decide(WorkbenchDecision.Approved)(ask, id, stop);
        };

        ApprovalAnswer? answer = await Channel().AskAsync(Question(), Token);

        Assert.Equal(ApprovalDecision.Granted, answer!.Decision);
        Assert.NotNull(meanwhile);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AWriteAskedInTheWorkbench_IsAuditedAsItsAndRunsOnlyWhenApproved(bool approved)
    {
        if (!approved)
        {
            _workbench.Answer = (_, _, _) => Task.FromResult<IReadOnlyList<byte[]>?>(null);
        }

        EplanInstanceInfo eplan = Eplan();
        eplan.ProcessId = Environment.ProcessId; // the pipeline asks this process's start time, as of a live EPLAN
        var bridge = new FakeBridgeClient { Status = new BridgeStatus([new BridgeEndpoint("p", eplan.ProcessId)], new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = eplan }, null) };
        bridge.Handlers["page.create.plan"] = _ => _plan;
        bridge.Handlers["page.create.execute"] = _ => new JsonObject { ["created"] = 1 };
        var dialog = new FakeApprovalChannel(EplanDialogChannel.ChannelName, ApprovalDecision.Granted);
        var request = new WriteRequest<object, object>("eplan_page_create", ToolTier.Write, "page.create.plan", "page.create.execute", new { pages = 1 }, plan => new { plan.Fingerprint }, false, TimeSpan.FromSeconds(60));

        Task<WriteOutcome<JsonObject>> run = Pipeline(bridge, Channel(), dialog).RunAsync<object, object, JsonObject>(request, Token);

        if (approved)
        {
            Assert.Equal(ExecutionState.Committed, (await run).Execution);
        }
        else
        {
            BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => run)).Error;
            Assert.Equal((ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        }

        JsonObject record = Directory.EnumerateFiles(_locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject()).First();
        Assert.Equal(("approval", WorkbenchChannel.ChannelName, approved ? "granted" : "cancelled"), (Text(record, "kind"), Text(record, "channel"), Text(record, "decision")));
        Assert.Equal(Assert.Single(_workbench.Asks).RequestId, Text(record, "requestId"));
        Assert.Equal(approved ? 1 : 0, bridge.Writes.Count);
        Assert.Empty(dialog.Questions);
    }

    /// <summary>Takes the question and sends nothing back, reading on.</summary>
    private static readonly Func<ApprovalAskParams, long, CancellationToken, Task<IReadOnlyList<byte[]>?>> _silent = (_, _, _) => Task.FromResult<IReadOnlyList<byte[]>?>([]);

    private static string Text(JsonObject record, string name) => record[name]!.GetValue<string>();

    private static EplanInstanceInfo Eplan() => new() { ProcessId = 4242, Version = "2026.0.3.25421", Variant = "Pro Panel" };

    private ApprovalQuestion Question() =>
        new("r1", "eplan_page_create", "h1", _plan, ["create page =A1&EFS1/5"], _clock.Now + _timeout) { Tier = ToolTier.Write, Eplan = Eplan() };

    /// <summary>The channel on the fake Workbench's pipe (or <paramref name="pipeName"/>), expecting this test process as its server.</summary>
    private WorkbenchChannel Channel(string? pipeName = null, string? image = null) =>
        new(pipeName ?? _workbench.PipeName, image ?? Environment.ProcessPath!, () => "claude-code", _clock, NullLogger.Instance);

    private ApprovalService Service(params IApprovalChannel[] channels) =>
        new(channels, new HostConfig([_locations.DefaultScratch], OutsideScratchWrites.Approve), _clock, NullLogger<ApprovalService>.Instance);

    private WritePipeline Pipeline(FakeBridgeClient bridge, params IApprovalChannel[] channels)
    {
        var audit = new AuditLog(_locations.Audit, TimeProvider.System);
        var config = new HostConfig([_locations.DefaultScratch], OutsideScratchWrites.Approve);
        return new WritePipeline(
            bridge,
            config,
            SuspensionStore.For(_locations),
            audit,
            new DanglingWrites(bridge, SuspensionStore.For(_locations), audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new ApprovalService(channels, config, _clock, NullLogger<ApprovalService>.Instance),
            new BackupFolders(_locations, TimeProvider.System),
            TimeProvider.System,
            NullLogger<WritePipeline>.Instance);
    }

    /// <summary>A service provider without an MCP server, as in every test: the client then has no name.</summary>
    private sealed class ServiceProviderStub : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
