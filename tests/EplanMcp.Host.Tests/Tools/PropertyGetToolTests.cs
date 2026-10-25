using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PropertyGetToolTests
{
    private const string Query = """{"target":{"project_object":true},"properties":[{"property_id":10901}]}""";
    private static readonly JsonSerializerOptions _requestJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private readonly ManualTime _clock = new(DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task Validation_ReportsWholeBatchPointersBeforeConnecting()
    {
        var bridge = new FakeBridgeClient();
        JsonElement error = Error(await Read(bridge, new(_clock), """
            {"queries":[{"target":{"project_object":true},"properties":[{"property_name":"BAD"}]}],"limit":201,"language":"bad","unknown":true}
            """));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        string[] paths = PropertyGetRequestTests.Violations(error).Select(v => v.Path).ToArray();
        Assert.Contains("/limit", paths);
        Assert.Contains("/language", paths);
        Assert.Contains("/unknown", paths);
        Assert.Contains("/queries/0/properties/0/property_name", paths);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task Paging_FollowsSparseIndexesAcrossBlocksAndKeepsExactTotalWhenKnown()
    {
        FakeBridgeClient bridge = Bridge(8, 3);
        var positions = new List<int>();
        Func<object?, object> handler = bridge.Handlers[OperationNames.PropertyRead];
        bridge.Handlers[OperationNames.PropertyRead] = a =>
        {
            positions.Add(((JsonObject)a!)["indexOffset"]!.GetValue<int>());
            return handler(a);
        };
        var paging = new PropertySchemaPaging(_clock);
        var indexes = new List<int>();
        string? cursor = null;
        do
        {
            JsonElement page = Data(await Read(bridge, paging, Request(cursor, limit: 2)));
            indexes.AddRange(page.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("index").GetInt32()));
            Assert.Equal("Line1", page.GetProperty("project").GetString());
            Assert.Empty(page.GetProperty("warnings").EnumerateArray());
            cursor = page.GetProperty("cursor").GetString();
            Assert.Equal(cursor is not null, page.GetProperty("truncated").GetBoolean());
            if (page.GetProperty("total").ValueKind == JsonValueKind.Number)
            {
                Assert.Equal(8, page.GetProperty("total").GetInt32());
            }
        }
        while (cursor is not null);

        Assert.Equal(Enumerable.Range(0, 8).Select(i => 10 + 2 * i), indexes);
        Assert.Contains(3, positions);
        Assert.Contains(6, positions);
    }

    [Fact]
    public async Task Cursor_NormalizesNamesMemberOrderWhitespaceAndDefaultFormat_AndAllowsChangingLimit()
    {
        FakeBridgeClient bridge = Bridge();
        var paging = new PropertySchemaPaging(_clock);
        string named = Request(limit: 1).Replace("\"property_id\":10901", "\"property_name\":\"PROJ_SUPPLEMENTARYFIELD\"", StringComparison.Ordinal);
        string cursor = Cursor(Data(await Read(bridge, paging, named)));
        JsonElement next = Data(await Read(bridge, paging, Request(cursor, limit: 3)));
        Assert.Equal([12, 14, 16], next.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("index").GetInt32()));
        string reordered = "{ \"response_format\":\"concise\",\"cursor\":" + JsonSerializer.Serialize(cursor)
            + ",\"queries\":[{\"properties\":[{\"property_id\":10901}],\"target\":{\"project_object\":true}}],\"limit\":2 }";
        Assert.Equal(12, Data(await Read(bridge, paging, reordered)).GetProperty("items")[0].GetProperty("index").GetInt32());
    }

    [Fact]
    public async Task Cursor_RejectsChangedRequestLanguageFormatSessionProjectInstanceTamperingAndExpiry()
    {
        FakeBridgeClient bridge = Bridge();
        var paging = new PropertySchemaPaging(_clock);
        string cursor = Cursor(Data(await Read(bridge, paging, Request(limit: 1))));
        Invalid(await Read(bridge, paging, Request(cursor).Replace("10901", "10013", StringComparison.Ordinal)));
        Invalid(await Read(bridge, paging, Request(cursor, language: "zh_CN")));
        Invalid(await Read(bridge, paging, Request(cursor, format: "detailed")));
        Invalid(await Read(bridge, new(_clock), Request(cursor)));
        Invalid(await Read(bridge, paging, Request("x" + cursor[1..])));
        Invalid(await Read(bridge, paging, Request("!")));
        Invalid(await Read(bridge, paging, Request(new string('x', 1025))));
        bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Name = "Line1", Path = "X:/different/Line1" } };
        Invalid(await Read(bridge, paging, Request(cursor)));
        bridge.Handlers[OperationNames.ProjectGet] = _ => Project();
        bridge.Status = Status("another-pipe");
        Invalid(await Read(bridge, paging, Request(cursor)));
        bridge.Status = Status();
        _clock.Advance(TimeSpan.FromMinutes(10));
        Invalid(await Read(bridge, paging, Request(cursor)));
    }

    [Fact]
    public async Task Cursor_RejectsChangedConsumedContentOrShortenedEnumeration_ButIgnoresObservationTime()
    {
        FakeBridgeClient bridge = Bridge();
        var paging = new PropertySchemaPaging(_clock);
        string cursor = Cursor(Data(await Read(bridge, paging, Request(limit: 2))));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Data(await Read(bridge, paging, Request(cursor, limit: 1)));
        bridge.Handlers[OperationNames.PropertyRead] = a => Block((JsonObject)a!, 8, 3, "changed");
        Invalid(await Read(bridge, paging, Request(cursor)));
        bridge.Handlers[OperationNames.PropertyRead] = a => Block((JsonObject)a!, 1, 3, "safe");
        Invalid(await Read(bridge, paging, Request(cursor)));
    }

    [Theory]
    [InlineData("concise", 5000)]
    [InlineData("detailed", 20000)]
    public async Task Budget_CountsBothCopiesSummaryAndCursor_AndContinuesWithinAnAlreadyReadBlock(string format, int budget)
    {
        FakeBridgeClient bridge = Bridge(18, 18, new string('值', 500));
        var paging = new PropertySchemaPaging(_clock);
        var indexes = new List<int>();
        string? cursor = null;
        int pages = 0;
        do
        {
            CallToolResult call = await Read(bridge, paging, Request(cursor, limit: 200, format: format));
            JsonElement data = Data(call);
            Assert.InRange(ResultShaper.EstimateTokens(data.GetRawText())
                + call.Content.Cast<TextContentBlock>().Sum(b => ResultShaper.EstimateTokens(b.Text)), 1, budget);
            Assert.Equal(18, data.GetProperty("total").GetInt32());
            Assert.NotEmpty(data.GetProperty("items").EnumerateArray());
            indexes.AddRange(data.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("index").GetInt32()));
            cursor = data.GetProperty("cursor").GetString();
            if (cursor is not null)
            {
                Assert.Contains("cursor", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
            }
            Assert.InRange(++pages, 1, 18);
        }
        while (cursor is not null);
        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 18).Select(i => 10 + 2 * i), indexes);
    }

    [Fact]
    public async Task SingleOversizedRow_FailsExplicitlyInsteadOfReturningAnUnusableEmptyPage()
    {
        FakeBridgeClient bridge = Bridge(1, 1);
        bridge.Handlers[OperationNames.PropertyRead] = a =>
        {
            PropertyReadData data = Block((JsonObject)a!, 1, 1, "safe");
            data.Items[0].Texts = [.. Enumerable.Range(0, 100).Select(_ => new SnapshotText { Language = "en_US", Text = new string('值', 500) })];
            return data;
        };
        JsonElement error = Invalid(await Read(bridge, new(_clock), Request(format: "detailed")));
        Assert.Contains("row exceeds", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("restart", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    public async Task InvalidOrNonProgressingBridgeContinuation_IsAnExplicitFailure(int selector, int index)
    {
        FakeBridgeClient bridge = Bridge();
        bridge.Handlers[OperationNames.PropertyRead] = _ => new PropertyReadData { NextSelector = selector, NextIndexOffset = index };
        JsonElement error = Error(await Read(bridge, new(_clock), Request()));
        Assert.Equal(ErrorReason.EplanOperationFailed, error.GetProperty("reason").GetString());
        Assert.Single(bridge.Calls, c => c.Operation == OperationNames.PropertyRead);
    }

    [Fact]
    public async Task BridgeBlockOverOneMiB_IsRejectedBeforeMapping()
    {
        FakeBridgeClient bridge = Bridge(1, 1, new string('x', 1024 * 1024));
        Assert.Equal(ErrorReason.EplanOperationFailed, Error(await Read(bridge, new(_clock), Request())).GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(ErrorReason.AmbiguousName, "/queries/0/target")]
    [InlineData(ErrorReason.DeviceNotFound, "/queries/0/target")]
    [InlineData(ErrorReason.PageNotFound, "/queries/0/target")]
    [InlineData(ErrorReason.PlanStale, "/queries/0/target")]
    [InlineData(ErrorReason.InvalidArgument, "/queries/0")]
    [InlineData(ErrorReason.PolicyDenied, "/queries/0")]
    public async Task BridgeError_ThroughTool_PreservesReasonCandidatesAndOriginalSelectorPointer(string reason, string expectedPath)
    {
        FakeBridgeClient bridge = Bridge();
        string candidates = "[{\"project\":\"Line1\",\"kind\":\"function\",\"type\":17,\"oid\":42}]";
        bridge.Handlers[OperationNames.PropertyRead] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = reason,
            Message = "Select a fresh locator or correct the property index.",
            Metadata = new()
            {
                ["candidates"] = candidates,
                ["violations"] = "[{\"path\":\"/properties/0/index\",\"reason\":\"INVALID_ARGUMENT\",\"message\":\"Invalid index.\"}]",
            },
        });
        string request = Request().Replace("{\"property_id\":10901}", "{\"property_id\":10901},{\"property_id\":10013}", StringComparison.Ordinal);
        JsonElement error = Error(await Read(bridge, new(_clock), request));
        Assert.Equal(reason, error.GetProperty("reason").GetString());
        Assert.Equal(expectedPath, error.GetProperty("metadata").GetProperty("pointer").GetString());
        Assert.Equal(candidates, error.GetProperty("metadata").GetProperty("candidates").GetString());
        Assert.Equal("/queries/0/properties/1/index", Assert.Single(PropertyGetRequestTests.Violations(error)).Path);
        Assert.Equal(JsonValueKind.Null, error.GetProperty("execution").ValueKind);
    }

    [Fact]
    public async Task RefusedRow_KeepsReasonAndNeverExposesValueViews()
    {
        FakeBridgeClient bridge = Bridge();
        bridge.Handlers[OperationNames.PropertyRead] = _ => new PropertyReadData
        {
            Items = [new("read_refused") { Target = new(projectObject: true), PropertyId = 3010, Reason = "user_metadata" }],
        };
        JsonElement row = Assert.Single(Data(await Read(bridge, new(_clock), Request(format: "detailed"))).GetProperty("items").EnumerateArray());
        Assert.Equal("read_refused", row.GetProperty("status").GetString());
        Assert.Equal("user_metadata", row.GetProperty("reason").GetString());
        foreach (string field in new[] { "value", "display", "texts", "unit", "format", "language" })
        {
            Assert.Equal(JsonValueKind.Null, row.GetProperty(field).ValueKind);
        }
    }

    private FakeBridgeClient Bridge(int rows = 8, int blockSize = 3, string text = "safe")
    {
        var bridge = new FakeBridgeClient { Status = Status() };
        bridge.Handlers[OperationNames.ProjectGet] = _ => Project();
        bridge.Handlers[OperationNames.PropertyRead] = a => Block((JsonObject)a!, rows, blockSize, text);
        return bridge;
    }

    private PropertyReadData Block(JsonObject args, int rows, int blockSize, string text)
    {
        int offset = args["indexOffset"]!.GetValue<int>();
        int count = Math.Min(blockSize, rows - offset);
        return new()
        {
            Items = [.. Enumerable.Range(offset, count).Select(i => new PropertyReadRow("value", new("text", text: text))
            {
                Target = new(projectObject: true),
                PropertyId = 10901,
                Index = 10 + 2 * i,
                ValueType = "text",
                ObservedAt = _clock.GetUtcNow(),
            })],
            NextSelector = offset + count < rows ? 0 : null,
            NextIndexOffset = offset + count,
        };
    }

    private static ProjectGetData Project() => new() { Project = new() { Name = "Line1", Path = "X:/fixture/Line1" } };

    private static BridgeStatus Status(string pipe = "test-pipe") => new([new(pipe, 7)], new() { Eplan = new() { ProcessId = 7 } }, null);

    internal static string Request(string? cursor = null, int limit = 50, string format = "concise", string? language = null) => JsonSerializer.Serialize(new
    {
        queries = JsonSerializer.Deserialize<JsonElement>("[" + Query + "]"),
        limit,
        response_format = format,
        cursor,
        language,
    }, _requestJson);

    private static Task<CallToolResult> Read(FakeBridgeClient bridge, PropertySchemaPaging paging, string json) =>
        PropertyGetTools.ReadAsync(bridge, paging, json, TestContext.Current.CancellationToken);

    internal static JsonElement Data(CallToolResult call)
    {
        ToolResultAssert.Succeeded(call);
        JsonElement data = call.StructuredContent!.Value;
        Assert.Equal(data.GetRawText(), ((TextContentBlock)call.Content[1]).Text);
        Assert.False(data.TryGetProperty("execution", out _));
        return data;
    }

    private static string Cursor(JsonElement data) => data.GetProperty("cursor").GetString()!;

    private static JsonElement Error(CallToolResult call) => PropertyGetRequestTests.Error(call);

    private static JsonElement Invalid(CallToolResult call)
    {
        JsonElement error = Error(call);
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        return error;
    }
}
