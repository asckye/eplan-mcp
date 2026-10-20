using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>How the router runs writes and answers outcome.get (ADR-0010 section 2).</summary>
[Trait("Size", "Small")]
public sealed class WriteInvokeTests
{
    private const string ExecuteOperation = "page.create.execute";

    private readonly ListLog _log = new();

    [Fact]
    public void AWrite_ThatCompletes_IsCommitted()
    {
        OperationRouter router = Router(new InlineDispatcher(), new FakeWrite(() => new JObject { ["pages"] = 2 }));

        JToken data = router.Invoke(ExecuteOperation, null, "r1");

        Assert.Equal(2, (int)data["pages"]!);
        Assert.Equal(OutcomeStatus.Committed, Outcome(router, "r1")["status"]!.ToString());
    }

    [Fact]
    public void AWrite_EplanCouldNotStart_IsNotStarted()
    {
        OperationRouter router = Router(new BusyDispatcher(BusyReason.ModalDialog), new FakeWrite(() => null));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(ExecuteOperation, null, "r1")).Error;

        Assert.Equal((ErrorReason.EplanBusy, true, ExecutionState.NotStarted), (error.Reason, error.Retryable, error.Execution));
        JObject outcome = Outcome(router, "r1");
        Assert.Equal((OutcomeStatus.NotStarted, ErrorReason.EplanBusy), (outcome["status"]!.ToString(), outcome["reason"]!.ToString()));
    }

    [Fact]
    public void AWrite_ThatFails_IsRolledBackUnlessItSaysOtherwise()
    {
        OperationRouter failing = Router(new InlineDispatcher(), new FakeWrite(() => throw new InvalidOperationException("boom")));
        OperationRouter early = Router(new InlineDispatcher(), new FakeWrite(() => throw Error(ErrorReason.ProjectNotOpen, ExecutionState.NotStarted)));
        OperationRouter unknown = Router(new InlineDispatcher(), new FakeWrite(() => throw Error(ErrorReason.OutcomeUnknown, ExecutionState.Unknown)));

        Assert.Equal((ErrorReason.Internal, ExecutionState.RolledBack), Failure(failing));
        Assert.Equal((ErrorReason.ProjectNotOpen, ExecutionState.NotStarted), Failure(early));
        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), Failure(unknown));
        Assert.Equal([OutcomeStatus.RolledBack, OutcomeStatus.NotStarted, OutcomeStatus.Unknown], new[] { failing, early, unknown }.Select(r => Outcome(r, "r1")["status"]!.ToString()));
    }

    [Fact]
    public void AWrite_WithARequestIdSeenBefore_NeverRunsAgain()
    {
        var write = new FakeWrite(() => null);
        OperationRouter router = Router(new InlineDispatcher(), write);
        router.Invoke(ExecuteOperation, null, "r1");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(ExecuteOperation, null, "r1")).Error;

        Assert.Equal(1, write.Runs);
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(OutcomeStatus.Committed, error.Metadata!["outcome"]);
    }

    [Fact]
    public void OutcomeGet_IsAnsweredWithoutEplansMainThread()
    {
        // A dispatcher that refuses everything: outcome.get never goes through it.
        OperationRouter router = Router(new BusyDispatcher(BusyReason.MainThreadBusy), new FakeWrite(() => null));

        Assert.Equal(OutcomeStatus.NotStarted, Outcome(router, "never-sent")["status"]!.ToString());
        Assert.Contains(OperationNames.OutcomeGet, router.SupportedOperations);
    }

    [Fact]
    public void OutcomeGet_WhileTheWriteRuns_SaysRunning()
    {
        OperationRouter? router = null;
        string? during = null;
        router = Router(new InlineDispatcher(), new FakeWrite(() =>
        {
            during = Outcome(router!, "r1")["status"]!.ToString();
            return null;
        }));

        router.Invoke(ExecuteOperation, null, "r1");

        Assert.Equal(OutcomeStatus.Running, during);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""{"requestId":""}""")]
    [InlineData("""{"requestId":7}""")]
    public void OutcomeGet_WithoutARequestId_IsInvalid(string? args)
    {
        OperationRouter router = Router(new InlineDispatcher());

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.OutcomeGet, args is null ? null : JToken.Parse(args), "c")).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
    }

    [Fact]
    public void AReadsError_CarriesNoExecutionState()
    {
        OperationRouter router = Router(new BusyDispatcher(BusyReason.ModalDialog));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.ProjectList, null, "c")).Error;

        Assert.Null(error.Execution);
    }

    private OperationRouter Router(IMainThreadDispatcher dispatcher, params IOperation[] writes) => new(
        [new ProjectListOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = "p", Current = true })), .. writes],
        dispatcher,
        null,
        _log,
        TimeSpan.FromSeconds(10));

    private static JObject Outcome(OperationRouter router, string requestId) =>
        (JObject)router.Invoke(OperationNames.OutcomeGet, new JObject { ["requestId"] = requestId }, "c");

    private static (string Reason, string? Execution) Failure(OperationRouter router)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(ExecuteOperation, null, "r1")).Error;
        return (error.Reason, error.Execution);
    }

    private static BridgeErrorException Error(string reason, string execution)
    {
        var error = new BridgeErrorException(reason, "m");
        error.Error.Execution = execution;
        return error;
    }

    private sealed class FakeWrite(Func<object?> work) : IWriteOperation
    {
        public int Runs { get; private set; }

        public string Name => ExecuteOperation;

        public object ParseArgs(JToken? args) => new();

        public object? Execute(object args)
        {
            Runs++;
            return work();
        }
    }
}
