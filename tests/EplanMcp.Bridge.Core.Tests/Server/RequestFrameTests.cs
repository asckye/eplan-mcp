using System.Globalization;
using System.Text;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Server;

[Trait("Size", "Small")]
public sealed class RequestFrameTests
{
    private const string Handshake = """{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"protocolVersion":1,"minProtocolVersion":1,"hostVersion":"t"}}""";
    private readonly ListLog _log = new();
    private readonly InlineDispatcher _dispatcher = new();

    [Theory]
    [InlineData("\"jsonrpc\":\"2.0\"")]
    [InlineData("\"id\":2")]
    [InlineData("\"method\":\"handshake\"")]
    [InlineData("\"params\":{\"protocolVersion\":1,\"minProtocolVersion\":1}")]
    [InlineData("\"\\u0069d\":2")]
    [InlineData("\"extra\":true")]
    [InlineData("\"ID\":2")]
    [InlineData("\"result\":{}")]
    public void Handle_DuplicateOrUnknownEnvelopeMember_ReturnsProtocolErrorWithLoggedDiagnostic(string member)
    {
        string raw = Handshake.Insert(Handshake.Length - 1, "," + member);

        JObject response = Send(Connection(), raw)!;

        Assert.NotNull(response["error"]);
        Assert.Equal(RpcException.InvalidRequest, (int)response["error"]!["code"]!);
        Assert.Equal("2.0", (string?)response["jsonrpc"]);
        Assert.Equal(JTokenType.Null, response["id"]!.Type);
        Assert.Null(response["result"]);
        string diagnosticId = (string)response["error"]!["data"]!["diagnosticId"]!;
        Assert.True(Guid.TryParseExact(diagnosticId, "N", out _));
        Assert.Contains(_log.Entries, entry => entry.Level == BridgeLogLevel.Warning && entry.Message.Contains(diagnosticId, StringComparison.Ordinal));
        Assert.Equal(0, _dispatcher.Calls);
    }

    [Theory]
    [InlineData("\"protocolVersion\":1,\"protocolVersion\":1,\"minProtocolVersion\":1,\"extra\":true")]
    [InlineData("\"protocolVersion\":1,\"minProtocolVersion\":1,\"extra\":{\"id\":1,\"id\":2}")]
    public void Handle_ExistingParamsSemantics_AreUnchanged(string parameters)
    {
        JObject response = Send(Connection(), "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"handshake\",\"params\":{" + parameters + "}}")!;

        Assert.Null(response["error"]);
        Assert.Equal(BridgeProtocol.CurrentVersion, (int)response["result"]!["protocolVersion"]!);
    }

    [Fact]
    public void Handle_NormalHandshakeAndInvoke_KeepWorking()
    {
        BridgeConnection connection = Connection();
        Assert.Null(Send(connection, Handshake)!["error"]);

        JObject response = Send(connection, """{"jsonrpc":"2.0","id":2,"method":"invoke","params":{"operation":"project.list","correlationId":"c","args":{}}}""")!;

        Assert.Equal(2, (int)response["id"]!);
        Assert.Equal("Line1", (string?)response["result"]!["data"]!["projects"]![0]!["name"]);
    }

    [Fact]
    public async Task Run_FrameBeyondProtocolLimit_ClosesBeforeReadingBodyAndLogsDiagnostic()
    {
        string header = "Content-Length: " + (BridgeProtocol.MaxFrameBytes + 1).ToString(CultureInfo.InvariantCulture) + "\r\n\r\n";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(header));

        await Connection().RunAsync(stream, TestContext.Current.CancellationToken);

        string message = Assert.Single(_log.Entries).Message;
        Assert.Contains("limit", message, StringComparison.Ordinal);
        Assert.Matches("[a-f0-9]{32}", message);
        Assert.Equal(header.Length, stream.Position);
        Assert.Equal(0, _dispatcher.Calls);
    }

    private BridgeConnection Connection()
    {
        IOperation[] operations = [new ProjectListOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = "p", Current = true }))];
        var router = new OperationRouter(operations, _dispatcher, null, _log, TimeSpan.FromSeconds(10));
        return new BridgeConnection(new BridgeServerOptions(), router, _log);
    }

    private static JObject? Send(BridgeConnection connection, string raw) => connection.Handle(Encoding.UTF8.GetBytes(raw));
}
