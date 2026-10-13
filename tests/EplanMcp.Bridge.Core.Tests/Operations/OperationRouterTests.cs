using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class OperationRouterTests
{
    private readonly ListLog _log = new();

    [Fact]
    public void Invoke_ProjectList_RunsOnTheDispatcherAndReturnsCamelCaseData()
    {
        var dispatcher = new InlineDispatcher();

        JToken data = Router(dispatcher).Invoke(OperationNames.ProjectList, new JObject(), "c");

        Assert.Equal(1, dispatcher.Calls);
        Assert.Equal("Line1", (string?)data["projects"]![0]!["name"]);
        Assert.True((bool)data["projects"]![0]!["current"]!);
    }

    [Theory]
    [InlineData(BusyReason.ModalDialog, "dialog")]
    [InlineData(BusyReason.MainThreadBusy, "did not respond within 10 s")]
    [InlineData(BusyReason.Reentrant, "still running")]
    public void Invoke_DispatcherBusy_IsRetryableEplanBusy(BusyReason reason, string hint)
    {
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Router(new BusyDispatcher(reason)).Invoke(OperationNames.ProjectList, null, "c"));

        Assert.Equal(ErrorReason.EplanBusy, ex.Error.Reason);
        Assert.True(ex.Error.Retryable);
        Assert.Contains(hint, ex.Error.Message, StringComparison.Ordinal);
        Assert.Equal(reason.ToString(), ex.Error.Metadata!["busyReason"]);
    }

    [Fact]
    public void Invoke_OperationThrowsBridgeError_PassesItThrough()
    {
        var failing = new DelegateOperation("x.fail", () => throw new BridgeErrorException(ErrorReason.LicenceMissing, "no licence"));

        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Router(new InlineDispatcher(), null, failing).Invoke("x.fail", null, "c"));

        Assert.Equal(ErrorReason.LicenceMissing, ex.Error.Reason);
    }

    [Fact]
    public void Invoke_UnknownException_IsInternalWithCorrelationIdAndNoDetails()
    {
        var failing = new DelegateOperation("x.fail", () => throw new InvalidOperationException(@"secret C:\Users\x"));

        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Router(new InlineDispatcher(), null, failing).Invoke("x.fail", null, "c-1"));

        Assert.Equal(ErrorReason.Internal, ex.Error.Reason);
        Assert.Equal("c-1", ex.Error.Metadata!["correlationId"]);
        Assert.DoesNotContain("secret", ex.Error.Message, StringComparison.Ordinal);
        Assert.Contains(_log.Entries, e => e.Exception is InvalidOperationException);
    }

    [Fact]
    public void Invoke_MappedException_UsesTheMappersReason()
    {
        var mapper = new FixedMapper(new BridgeError { Reason = ErrorReason.LockFailed, Message = "locked", Retryable = true });
        var failing = new DelegateOperation("x.fail", () => throw new InvalidOperationException("eplan"));

        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Router(new InlineDispatcher(), mapper, failing).Invoke("x.fail", null, "c"));

        Assert.Equal(ErrorReason.LockFailed, ex.Error.Reason);
        Assert.True(ex.Error.Retryable);
    }

    [Fact]
    public void Invoke_UnknownOperation_IsInvalidParams()
    {
        RpcException ex = Assert.Throws<RpcException>(() => Router(new InlineDispatcher()).Invoke("nope.nope", null, "c"));

        Assert.Equal(RpcException.InvalidParams, ex.Code);
    }

    [Fact]
    public void Invoke_ArgumentsNotAnObject_IsInvalidArgumentBeforeDispatch()
    {
        var dispatcher = new InlineDispatcher();

        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Router(dispatcher).Invoke(OperationNames.ProjectList, new JArray(1), "c"));

        Assert.Equal(ErrorReason.InvalidArgument, ex.Error.Reason);
        Assert.Equal(0, dispatcher.Calls);
    }

    private OperationRouter Router(IMainThreadDispatcher dispatcher, IExceptionMapper? mapper = null, params IOperation[] extra)
    {
        IOperation[] operations = [new ProjectListOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = "p", Current = true })), .. extra];
        return new OperationRouter(operations, dispatcher, mapper, _log, TimeSpan.FromSeconds(10));
    }
}
