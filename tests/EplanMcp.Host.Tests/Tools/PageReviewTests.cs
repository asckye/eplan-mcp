using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PageReviewTests
{
    private static readonly int[] _firstPosition = [8, 8], _secondPosition = [16, 8], _invalidPosition = [2];
    private readonly FakeBridgeClient _bridge = new();
    private readonly ManualTime _clock = new(DateTimeOffset.UnixEpoch);
    private static JsonObject Evidence(int count = 0) => new()
    {
        ["project"] = "Line1",
        ["page"] = "=F+A1/1",
        ["actual"] = JsonNode.Parse("""{"devices":[],"connections":[],"cables":[],"interruptions":[],"texts":[],"messages":[]}"""),
        ["issues"] = new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode?)new JsonObject { ["code"] = "overlap", ["severity"] = "warning", ["page"] = "=F+A1/1", ["position"] = new JsonArray(i, 8), ["objects"] = new JsonArray(), ["message"] = "Overlapping bounds", ["fix"] = new JsonObject { ["tool"] = "eplan_placement_update" } }).ToArray()),
    };
    public PageReviewTests() => _bridge.Handlers[OperationNames.PageReview] = _ => Evidence();
    private Task<CallToolResult> Review(int limit = 50, string? cursor = null, string[]? checks = null, JsonElement? expected = null, bool image = false, string format = "concise") =>
        PageReviewTools.ReviewAsync(_bridge, _clock, "Line1", "=F+A1/1", checks: checks, expected: expected, include_image: image, limit: limit, cursor: cursor, response_format: format, cancellationToken: TestContext.Current.CancellationToken);
    private static JsonElement Data(CallToolResult result) { Assert.False(result.IsError == true); return result.StructuredContent!.Value; }
    [Fact]
    public async Task Clean_ReportsResolvedProjectNoExecutionAndReadTimeout()
    {
        CallToolResult result = await Review();
        Assert.True(Data(result).GetProperty("summary").GetProperty("clean").GetBoolean());
        Assert.Equal("=F+A1/1: clean", ((TextContentBlock)result.Content[0]).Text);
        Assert.Equal("Line1", Data(result).GetProperty("project").GetString());
        Assert.False(Data(result).TryGetProperty("execution", out _));
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(_bridge.Timeouts));
        Assert.Equal(OperationNames.PageReview, Assert.Single(_bridge.Calls).Operation);
    }
    [Fact]
    public async Task SlowRead_ReportsAfterTwoSeconds_StopsAfterResult()
    {
        var reports = new Reports();
        _bridge.Handlers[OperationNames.PageReview] = _ => { _clock.Advance(TimeSpan.FromSeconds(7)); return Evidence(); };
        await PageReviewTools.ReviewAsync(_bridge, _clock, "Line1", "=F+A1/1", progress: reports, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal([2f, 7f], reports.Values.Select(v => v.Progress));
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(2, reports.Values.Count);
    }
    [Fact]
    public async Task CancelledRead_DoesNotContactBridgeOrLeaveProgressTimer()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PageReviewTools.ReviewAsync(_bridge, _clock, "Line1", "=F+A1/1", cancellationToken: cancelled.Token));
        Assert.Empty(_bridge.Calls);
        Assert.Equal(0, _clock.TimersCreated);
    }
    private sealed class Reports : IProgress<ProgressNotificationValue>
    {
        internal List<ProgressNotificationValue> Values { get; } = [];
        public void Report(ProgressNotificationValue value) => Values.Add(value);
    }
    [Fact]
    public async Task Pagination_SummaryCountsWholePage_RejectsChangedEvidenceAndArguments()
    {
        _bridge.Handlers[OperationNames.PageReview] = _ => Evidence(3);
        JsonElement first = Data(await Review(limit: 1));
        Assert.Equal(3, first.GetProperty("summary").GetProperty("warnings").GetInt32());
        Assert.Equal(3, first.GetProperty("total").GetInt32());
        string cursor = first.GetProperty("cursor").GetString()!;
        JsonElement next = Data(await Review(limit: 2, cursor: cursor));
        Assert.Equal(2, next.GetProperty("issues").GetArrayLength());
        Assert.False(next.GetProperty("truncated").GetBoolean());
        Assert.True((await Review(cursor: cursor, checks: ["off_grid"])).IsError);
        _bridge.Handlers[OperationNames.PageReview] = _ => Evidence(4);
        Assert.True((await Review(cursor: cursor)).IsError);
    }
    [Fact]
    public async Task Budget_ContinuesWithoutDroppingIssuesOrWholePageSummary()
    {
        _bridge.Handlers[OperationNames.PageReview] = _ =>
        {
            JsonObject data = Evidence(150);
            foreach (JsonNode? issue in data["issues"]!.AsArray())
            { issue!["message"] = new string('x', 500); }
            return data;
        };
        int seen = 0;
        string? cursor = null;
        do
        {
            CallToolResult result = await Review(limit: 200, cursor: cursor);
            JsonElement data = Data(result);
            Assert.True(ResultShaper.ResultTokens(data.GetRawText()) < ResultShaper.DefaultTokenBudget);
            Assert.Equal(150, data.GetProperty("summary").GetProperty("warnings").GetInt32());
            Assert.NotEmpty(data.GetProperty("issues").EnumerateArray());
            seen += data.GetProperty("issues").GetArrayLength();
            cursor = data.GetProperty("cursor").GetString();
        } while (cursor is not null);
        Assert.Equal(150, seen);
    }
    [Theory]
    [InlineData(ErrorReason.PageNotFound)]
    [InlineData(ErrorReason.ProjectNotOpen)]
    [InlineData(ErrorReason.EplanBusy)]
    [InlineData(ErrorReason.Timeout)]
    public async Task BridgeErrors_AreReadErrorsWithoutExecution(string reason)
    {
        _bridge.Failure = new() { Reason = reason, Message = "Retry after inspecting eplan_page_list." };
        CallToolResult result = await Review();
        Assert.True(result.IsError);
        Assert.Contains(reason, ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
        using JsonDocument error = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        Assert.Equal(JsonValueKind.Null, error.RootElement.GetProperty("error").GetProperty("execution").ValueKind);
    }
    [Fact]
    public async Task Expected_MissingItemsArePagedAlongWithIssues()
    {
        JsonElement target = JsonSerializer.SerializeToElement(new { texts = new[] { new { text = "A", position = _firstPosition }, new { text = "B", position = _secondPosition } } });
        JsonElement first = Data(await Review(limit: 1, expected: target));
        Assert.Equal("/texts/0", first.GetProperty("expected").GetProperty("missing")[0].GetProperty("path").GetString());
        Assert.Equal(2, first.GetProperty("summary").GetProperty("errors").GetInt32());
        Assert.False(first.GetProperty("summary").GetProperty("clean").GetBoolean());
        JsonElement second = Data(await Review(limit: 1, expected: target, cursor: first.GetProperty("cursor").GetString()));
        Assert.Equal("/texts/1", second.GetProperty("expected").GetProperty("missing")[0].GetProperty("path").GetString());
        Assert.True(Data(await Review(checks: ["off_grid"], expected: target)).GetProperty("summary").GetProperty("clean").GetBoolean());
    }
    [Fact]
    public async Task InvalidExpected_ReturnsEveryPointerBeforeCallingBridge()
    {
        CallToolResult result = await Review(expected: JsonSerializer.SerializeToElement(new { devices = new[] { new { tag = 3, position = _invalidPosition } }, typo = true }));
        string error = ((TextContentBlock)result.Content[0]).Text;
        Assert.True(result.IsError);
        Assert.Contains("/expected/devices/0/tag", error, StringComparison.Ordinal);
        Assert.Contains("/expected/devices/0/position", error, StringComparison.Ordinal);
        Assert.Contains("/expected/typo", error, StringComparison.Ordinal);
        Assert.Empty(_bridge.Calls);
    }
    [Fact]
    public async Task UnknownChecksAndDeferredImages_AreActionableArgumentErrors()
    {
        Assert.True((await Review(checks: ["unknown"])).IsError);
        Assert.True((await Review(image: true)).IsError);
        Assert.Empty(_bridge.Calls);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExpectedComparison_UsesToleranceAndUnorderedConnectionMultisets(bool matches)
    {
        JsonObject expected = JsonNode.Parse("""{"devices":[{"tag":"-K1","position":[8,8]}],"connections":[{"from":{"device":"-K1","pin":"1"},"to":{"device":"-K2","pin":""}}]}""")!.AsObject();
        JsonObject actual = JsonNode.Parse("""{"devices":[{"tag":"+A1-K1","position":[8.5,8]}],"connections":[{"to":{"device":"+A1-K1","pin":"1"},"from":{"device":"+A1-K2","pin":""}}]}""")!.AsObject();
        if (!matches)
        { actual["devices"]![0]!["position"]![0] = 8.51; }
        JsonArray differences = ExpectedReview.Compare(expected, actual, "=F+A1/1", 0.5);
        Assert.Equal(matches ? 0 : 1, differences.Count);
        actual["connections"]!.AsArray().Add(actual["connections"]![0]!.DeepClone());
        Assert.Contains(ExpectedReview.Compare(expected, actual, "=F+A1/1", 0.5), d => d!["kind"]!.ToString() == "extra");
    }
    [Theory]
    [InlineData("texts", "{\"text\":\"Note\",\"position\":[8,8],\"angle\":90}", "angle", "0")]
    [InlineData("devices", "{\"tag\":\"-K1\",\"position\":[8,8],\"symbol\":\"K\"}", "symbol", "\"M\"")]
    [InlineData("interruptions", "{\"name\":\"I1\",\"position\":[8,8]}", "position", "[16,8]")]
    public void ExpectedComparison_AllDrawingCategoriesHavePositiveAndNegativeCases(string category, string row, string field, string changed)
    {
        var target = new JsonObject { [category] = new JsonArray(JsonNode.Parse(row)) };
        JsonObject actual = target.DeepClone().AsObject();
        Assert.Empty(ExpectedReview.Compare(target, actual, "=F+A1/1", 0.5));
        actual[category]![0]![field] = JsonNode.Parse(changed);
        Assert.Equal("mismatched", Assert.Single(ExpectedReview.Compare(target, actual, "=F+A1/1", 0.5))!["kind"]!.ToString());
        Assert.Single(ExpectedReview.Compare(target, new JsonObject { [category] = new JsonArray() }, "=F+A1/1", 0.5));
    }
    [Fact]
    public void ExpectedComparison_CoreDifferenceCarriesNestedPathAndActual()
    {
        JsonObject expected = JsonNode.Parse("""{"cables":[{"designation":"-W1","cores":[{"from":{"device":"A","pin":"1"},"to":{"device":"B","pin":"2"},"label":"1","colour":"RD"}]}]}""")!.AsObject();
        JsonObject actual = expected.DeepClone().AsObject();
        Assert.Empty(ExpectedReview.Compare(expected, actual, "=F+A1/1", 0.5));
        actual["cables"]![0]!["cores"]![0]!["colour"] = "BK";
        JsonNode difference = Assert.Single(ExpectedReview.Compare(expected, actual, "=F+A1/1", 0.5))!;
        Assert.Equal("/cables/0/cores/0", difference["path"]!.ToString());
        Assert.Equal("BK", difference["actual"]!["colour"]!.ToString());
    }
}
