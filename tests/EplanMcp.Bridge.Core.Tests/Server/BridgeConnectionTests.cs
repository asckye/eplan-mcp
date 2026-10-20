using System.Text;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Server;

/// <summary>The JSON-RPC layer: handshake rules, request validation and how router outcomes become responses.</summary>
[Trait("Size", "Small")]
public sealed class BridgeConnectionTests
{
    private const string Handshake = """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1,"hostVersion":"t"}}""";

    private readonly ListLog _log = new();
    private readonly InlineDispatcher _dispatcher = new();

    [Fact]
    public void Handshake_CompatibleHost_ReturnsVersionAndEplanInfo()
    {
        JObject response = Send(Connection(), Handshake)!;

        Assert.Equal(1, (int)response["id"]!);
        Assert.Equal(BridgeProtocol.CurrentVersion, (int)response["result"]!["protocolVersion"]!);
        Assert.Equal("9.9.9", (string?)response["result"]!["bridgeVersion"]);
        Assert.Equal(".NETFramework,Version=v4.8.1", (string?)response["result"]!["buildTarget"]);
        Assert.Equal(4242, (int)response["result"]!["eplan"]!["processId"]!);
    }

    [Fact]
    public void Handshake_AnnouncesTheRegisteredOperationsAndOutcomeGetSorted()
    {
        JObject response = Send(Connection(new DelegateOperation("device.list", () => null), new DelegateOperation("a.first", () => null)), Handshake)!;

        // outcome.get comes with every Bridge that records writes; the Host sends writes only to those (ADR-0010).
        Assert.Equal(["a.first", "device.list", OperationNames.OutcomeGet, OperationNames.ProjectList], response["result"]!["operations"]!.Values<string>());
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(0, 0)]
    public void Handshake_HostOutsideOurVersion_IsBridgeIncompatibleAndInvokeStaysLocked(int min, int max)
    {
        BridgeConnection connection = Connection();

        JObject response = Send(connection, $$$"""{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":{{{max}}},"minProtocolVersion":{{{min}}}}}""")!;

        AssertBusinessError(response, ErrorReason.BridgeIncompatible);
        AssertRpcError(Send(connection, Invoke(OperationNames.ProjectList)), BridgeProtocol.NotHandshakenErrorCode);
    }

    [Fact]
    public void FailedSecondHandshake_LocksInvokeAgain()
    {
        BridgeConnection connection = Connection();
        Send(connection, Handshake);

        Send(connection, """{"jsonrpc":"2.0","id":3,"method":"handshake","params":{"protocolVersion":3,"minProtocolVersion":2}}""");

        AssertRpcError(Send(connection, Invoke(OperationNames.ProjectList)), BridgeProtocol.NotHandshakenErrorCode);
    }

    [Fact]
    public void Invoke_CorrelationIdNotAString_IsInvalidParams()
    {
        BridgeConnection connection = Connection();
        Send(connection, Handshake);

        JObject? response = Send(connection, """{"jsonrpc":"2.0","id":2,"method":"invoke","params":{"operation":"project.list","correlationId":{"x":1},"args":{}}}""");

        AssertRpcError(response, -32602);
        Assert.DoesNotContain(_log.Entries, e => e.Level == Diagnostics.BridgeLogLevel.Error);
    }

    [Fact]
    public void Invoke_BeforeHandshake_IsRejectedWithoutRunningAnything()
    {
        AssertRpcError(Send(Connection(), Invoke(OperationNames.ProjectList)), BridgeProtocol.NotHandshakenErrorCode);
        Assert.Equal(0, _dispatcher.Calls);
    }

    [Fact]
    public void Invoke_AfterHandshake_WrapsTheOperationDataInResult()
    {
        BridgeConnection connection = Connection();
        Send(connection, Handshake);

        JObject response = Send(connection, Invoke(OperationNames.ProjectList))!;

        Assert.Equal(2, (int)response["id"]!);
        Assert.Equal("Line1", (string?)response["result"]!["data"]!["projects"]![0]!["name"]);
    }

    [Fact]
    public void Invoke_BusinessError_BecomesErrorDataWithTheSameMessage()
    {
        BridgeConnection connection = Connection(new DelegateOperation("x.fail", () => throw new BridgeErrorException(ErrorReason.LicenceMissing, "no licence")));
        Send(connection, Handshake);

        JObject response = Send(connection, Invoke("x.fail"))!;

        JToken data = AssertBusinessError(response, ErrorReason.LicenceMissing);
        Assert.Equal("no licence", (string?)response["error"]!["message"]);
        Assert.Equal("no licence", (string?)data["message"]);
    }

    [Theory]
    [InlineData("not json", -32700)]
    [InlineData("[1,2]", -32700)]
    [InlineData("""{"id":1,"method":"handshake"}""", -32600)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":5}""", -32600)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"shutdown"}""", -32601)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"handshake","params":[1]}""", -32602)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":"x"}}""", -32602)]
    public void MalformedRequests_GetJsonRpcErrors(string raw, int code)
    {
        AssertRpcError(Send(Connection(), raw), code);
    }

    [Fact]
    public void Notification_GetsNoResponse()
    {
        Assert.Null(Send(Connection(), """{"jsonrpc":"2.0","method":"cancel","params":{"id":3}}"""));
    }

    private BridgeConnection Connection(params IOperation[] extra)
    {
        IOperation[] operations = [new ProjectListOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = "p", Current = true })), .. extra];
        var router = new OperationRouter(operations, _dispatcher, null, _log, TimeSpan.FromSeconds(10));
        var options = new BridgeServerOptions
        {
            BridgeVersion = "9.9.9",
            Eplan = new EplanInstanceInfo { ProcessId = 4242, Version = "2026" },
            BuildTarget = ".NETFramework,Version=v4.8.1",
        };
        return new BridgeConnection(options, router, _log);
    }

    private static string Invoke(string operation) => new JObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 2,
        ["method"] = "invoke",
        ["params"] = new JObject { ["operation"] = operation, ["correlationId"] = "c", ["args"] = new JObject() },
    }.ToString(Newtonsoft.Json.Formatting.None);

    private static JObject? Send(BridgeConnection connection, string raw) => connection.Handle(Encoding.UTF8.GetBytes(raw));

    private static void AssertRpcError(JObject? response, int code)
    {
        Assert.NotNull(response);
        Assert.Null(response["result"]);
        Assert.Equal(code, (int)response["error"]!["code"]!);
    }

    private static JToken AssertBusinessError(JObject? response, string reason)
    {
        AssertRpcError(response, BridgeProtocol.BusinessErrorCode);
        JToken data = response!["error"]!["data"]!;
        Assert.Equal(reason, (string?)data["reason"]);
        return data;
    }
}
