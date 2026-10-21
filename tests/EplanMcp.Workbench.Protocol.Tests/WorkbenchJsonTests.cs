using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Workbench.Protocol.Tests;

/// <summary>
/// The protocol's messages on the wire (ADR-0014 decision 3): what each looks like, that each reads back as written, and
/// that strict reading refuses unknown, duplicate and missing fields, wrong types and values, and broken envelopes.
/// </summary>
[Trait("Size", "Small")]
public sealed class WorkbenchJsonTests
{
    private static readonly ApprovalAskParams _ask = new(
        "r1",
        QuestionKind.Approval,
        "eplan_page_delete",
        QuestionTier.Destructive,
        new ProjectInfo("Line1", @"D:\Projects\Line1"),
        new EplanInstance("2026.0.3.25421", "Pro Panel", 4242),
        "claude-code",
        ["删除页 =A1&EFS1/5", "Delete page =A1&EFS1/6"],
        @"D:\EplanMcp\backups\Line1",
        null,
        "f1",
        "h1",
        1_793_000_000_000);

    [Fact]
    public void ApprovalAsk_OnTheWire_HasCamelCaseFieldsAndSnakeCaseValues()
    {
        JsonNode sent = JsonNode.Parse(WorkbenchJson.Request(7, WorkbenchProtocol.ApprovalAskMethod, _ask))!;

        string expected = """
            {"jsonrpc":"2.0","id":7,"method":"approval.ask","params":{"requestId":"r1","kind":"approval","tool":"eplan_page_delete",
            "tier":"destructive","project":{"name":"Line1","path":"D:\\Projects\\Line1"},
            "eplan":{"version":"2026.0.3.25421","variant":"Pro Panel","processId":4242},"clientName":"claude-code",
            "changes":["删除页 =A1&EFS1/5","Delete page =A1&EFS1/6"],"backupFolder":"D:\\EplanMcp\\backups\\Line1","suspension":null,
            "fingerprint":"f1","argumentsHash":"h1","deadlineUnixMs":1793000000000}}
            """;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), sent), sent.ToJsonString());
    }

    [Fact]
    public void EveryMessage_ReadsBackAsWritten()
    {
        var release = _ask with
        {
            Kind = QuestionKind.SuspensionRelease,
            Tier = QuestionTier.Write,
            ClientName = null,
            BackupFolder = null,
            Suspension = new SuspensionRecord("earlier", "eplan_page_create", 1_792_999_000_000, null, 4242),
        };
        var handshake = new WorkbenchHandshake(1, 1, "0.1.0", 99);
        var welcome = new WorkbenchHandshakeResult(1, "0.1.0", Accepting: true);
        var answer = new ApprovalAskResult("r1", "f1", "h1", WorkbenchDecision.Approved);
        var outcome = new ApprovalOutcomeParams("r1", WriteExecution.RolledBack, "PLAN_STALE");

        AssertSame(_ask, Params<ApprovalAskParams>(WorkbenchJson.Request(1, WorkbenchProtocol.ApprovalAskMethod, _ask)));
        AssertSame(release, Params<ApprovalAskParams>(WorkbenchJson.Request(1, WorkbenchProtocol.ApprovalAskMethod, release)));
        Assert.Equal(handshake, Params<WorkbenchHandshake>(WorkbenchJson.Request(1, WorkbenchProtocol.HandshakeMethod, handshake)));
        Assert.Equal(new CancelParams("r1"), Params<CancelParams>(WorkbenchJson.Notification(WorkbenchProtocol.CancelMethod, new CancelParams("r1"))));
        Assert.Equal(outcome, Params<ApprovalOutcomeParams>(WorkbenchJson.Notification(WorkbenchProtocol.ApprovalOutcomeMethod, outcome)));
        Assert.Equal(welcome, WorkbenchJson.As<WorkbenchHandshakeResult>(WorkbenchJson.Read(WorkbenchJson.Result(1, welcome)).Result));
        Assert.Equal(answer, WorkbenchJson.As<ApprovalAskResult>(WorkbenchJson.Read(WorkbenchJson.Result(1, answer)).Result));
    }

    [Theory]
    [InlineData(WorkbenchDecision.Approved, "approved")]
    [InlineData(WorkbenchDecision.Rejected, "rejected")]
    [InlineData(WorkbenchDecision.Closed, "closed")]
    [InlineData(WorkbenchDecision.Expired, "expired")]
    public void Decisions_AreWrittenAsTheirNames(WorkbenchDecision decision, string written)
    {
        JsonNode message = JsonNode.Parse(WorkbenchJson.Result(1, new ApprovalAskResult("r", "f", "h", decision)))!;

        Assert.Equal(written, message["result"]!["decision"]!.GetValue<string>());
    }

    [Fact]
    public void Envelopes_AreReadAsRequestNotificationOrResponse()
    {
        RpcMessage request = WorkbenchJson.Read(WorkbenchJson.Request(3, WorkbenchProtocol.HandshakeMethod, new WorkbenchHandshake(1, 1, "v", 1)));
        RpcMessage notification = WorkbenchJson.Read(WorkbenchJson.Notification(WorkbenchProtocol.CancelMethod, new CancelParams("r")));
        RpcMessage error = WorkbenchJson.Read(WorkbenchJson.Error(3, WorkbenchProtocol.UnsupportedVersionErrorCode, "version 2 only"));
        RpcMessage unreadable = WorkbenchJson.Read(WorkbenchJson.Error(null, -32700, "parse error"));

        Assert.Equal((3L, "handshake"), (request.Id, request.Method));
        Assert.Equal((null, "cancel"), (notification.Id, notification.Method));
        Assert.Equal((3L, new RpcError(-32001, "version 2 only")), (error.Id, error.Error));
        Assert.Equal((null, -32700), (unreadable.Id, unreadable.Error!.Code));
    }

    public static TheoryData<string, string> InvalidParams => new()
    {
        { "unknown field", """{"requestId":"r","fingerprint":"f","argumentsHash":"h","decision":"approved","extra":1}""" },
        { "duplicate field", """{"requestId":"r","fingerprint":"f","argumentsHash":"h","decision":"rejected","decision":"approved"}""" },
        { "missing field", """{"requestId":"r","fingerprint":"f","decision":"approved"}""" },
        { "null for text", """{"requestId":null,"fingerprint":"f","argumentsHash":"h","decision":"approved"}""" },
        { "other case of a name", """{"RequestId":"r","fingerprint":"f","argumentsHash":"h","decision":"approved"}""" },
        { "number for text", """{"requestId":1,"fingerprint":"f","argumentsHash":"h","decision":"approved"}""" },
        { "unknown decision", """{"requestId":"r","fingerprint":"f","argumentsHash":"h","decision":"maybe"}""" },
        { "decision as number", """{"requestId":"r","fingerprint":"f","argumentsHash":"h","decision":0}""" },
        { "decision as numeric text", """{"requestId":"r","fingerprint":"f","argumentsHash":"h","decision":"0"}""" },
        { "two decisions", """{"requestId":"r","fingerprint":"f","argumentsHash":"h","decision":"rejected, approved"}""" },
        { "decision in other case", """{"requestId":"r","fingerprint":"f","argumentsHash":"h","decision":"Approved"}""" },
        { "not an object", "[]" },
        { "null", "null" },
    };

    [Theory]
    [MemberData(nameof(InvalidParams))]
    public void AnInvalidAnswer_IsAProtocolError(string why, string result)
    {
        byte[] frame = Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","id":1,"result":{{result}}}""");

        Assert.Throws<WorkbenchProtocolException>(() => WorkbenchJson.As<ApprovalAskResult>(WorkbenchJson.Read(frame).Result));
        Assert.NotEmpty(why);
    }

    public static TheoryData<string, string> InvalidAsks => new()
    {
        { "null change line", """{"changes":[null]}""" },
        { "changes missing", """{"changes":null}""" },
        { "unknown field in a nested object", """{"project":{"name":"Line1","path":"p","guid":"x"}}""" },
        { "deadline as text", """{"deadlineUnixMs":"1793000000000"}""" },
        { "fractional process ID", """{"eplan":{"version":"v","variant":null,"processId":1.5}}""" },
        { "tier code", """{"tier":"code"}""" },
    };

    [Theory]
    [MemberData(nameof(InvalidAsks))]
    public void AnInvalidQuestion_IsAProtocolError(string why, string replace)
    {
        JsonObject ask = JsonNode.Parse(WorkbenchJson.Request(1, WorkbenchProtocol.ApprovalAskMethod, _ask))!["params"]!.AsObject();
        foreach ((string name, JsonNode? value) in JsonNode.Parse(replace)!.AsObject())
        {
            ask[name] = value?.DeepClone();
        }

        byte[] frame = Encoding.UTF8.GetBytes($$"""{"jsonrpc":"2.0","id":1,"method":"approval.ask","params":{{ask.ToJsonString()}}}""");

        Assert.Throws<WorkbenchProtocolException>(() => Params<ApprovalAskParams>(frame));
        Assert.NotEmpty(why);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{},"extra":true}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"id":2,"result":{}}""")]
    [InlineData("""{"jsonrpc":"1.0","id":1,"result":{}}""")]
    [InlineData("""{"id":1,"result":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{},"error":{"code":1,"message":"m"}}""")]
    [InlineData("""{"jsonrpc":"2.0","result":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":"1","result":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1.5,"result":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"cancel","result":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"params":{},"result":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"error":{"code":1,"message":"m","data":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"error":{"code":1}}""")]
    [InlineData("""{"jsonrpc":"2.0","method":"cancel","params":{"requestId":"r","requestId":"s"}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{}""")]
    [InlineData("")]
    [InlineData("null")]
    public void AnInvalidEnvelope_IsAProtocolError(string json)
    {
        Assert.Throws<WorkbenchProtocolException>(() =>
        {
            RpcMessage message = WorkbenchJson.Read(Encoding.UTF8.GetBytes(json));
            _ = WorkbenchJson.As<CancelParams>(message.Params);
        });
    }

    [Fact]
    public void InvalidUtf8_IsAProtocolError()
    {
        byte[] frame = [.. Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"cancel","params":{"requestId":" """), 0xC3, 0x28, .. Encoding.UTF8.GetBytes("\"}}")];

        Assert.Throws<WorkbenchProtocolException>(() => WorkbenchJson.Read(frame));
    }

    [Fact]
    public void AMessageWithoutParams_IsAProtocolErrorWhenTheyAreRead()
    {
        RpcMessage message = WorkbenchJson.Read(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"cancel"}"""));

        Assert.Throws<WorkbenchProtocolException>(() => WorkbenchJson.As<CancelParams>(message.Params));
    }

    private static T Params<T>(byte[] frame) => WorkbenchJson.As<T>(WorkbenchJson.Read(frame).Params);

    /// <summary>Records compare their change lists by reference; compare them as JSON.</summary>
    private static void AssertSame(ApprovalAskParams expected, ApprovalAskParams actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected, WorkbenchJson.Options), JsonSerializer.Serialize(actual, WorkbenchJson.Options));
}
