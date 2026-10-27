using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PageDrawingToolTests
{
    [Theory]
    [InlineData(0, null)]
    [InlineData(201, null)]
    [InlineData(50, " ")]
    public async Task Arguments_InvalidDrawingOptions_DoNotCallBridge(int limit, string? cursor)
    {
        var bridge = new FakeBridgeClient();
        CallToolResult result = await Read(bridge, limit, cursor);
        Assert.True(result.IsError);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task Cursor_WithoutDiscovery_IsRejectedBeforeBridge()
    {
        var bridge = new FakeBridgeClient();
        Assert.True((await PageTools.PageGetAsync(bridge, "P", drawing_cursor: "cursor", cancellationToken: TestContext.Current.CancellationToken)).IsError);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task DefaultCall_OmitsDrawingsAndKeepsExactLegacyResponse()
    {
        FakeBridgeClient bridge = Bridge(3);
        CallToolResult legacy = await PageTools.PageGetAsync(bridge, "P", cancellationToken: TestContext.Current.CancellationToken);
        CallToolResult explicitFalse = await PageTools.PageGetAsync(bridge, "P", include_drawings: false, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(legacy.StructuredContent!.Value.GetRawText(), explicitFalse.StructuredContent!.Value.GetRawText());
        Assert.Equal("""{"project":"Line1","page":{"name":"P","type":"Circuit","typeName":null,"description":null},"devices":[],"deviceCount":0,"properties":null,"truncated":false,"hint":null}""", legacy.StructuredContent.Value.GetRawText());
        Assert.Equal([OperationNames.PageGet, OperationNames.PageGet], bridge.Calls.Select(c => c.Operation));
    }

    [Theory]
    [InlineData("concise", 5000)]
    [InlineData("detailed", 20000)]
    public async Task Paging_BudgetCuts_ResumeAtFirstOmittedObject(string format, int budget)
    {
        FakeBridgeClient bridge = Bridge(207, textLength: 300);
        string? cursor = null;
        var ids = new List<long>();
        int calls = 0;
        do
        {
            JsonElement data = Data(await Read(bridge, 200, cursor, format));
            Assert.InRange(ResultShaper.ResultTokens(data.GetRawText()), 1, budget);
            JsonElement drawings = data.GetProperty("drawings");
            Assert.Equal(207, drawings.GetProperty("total").GetInt32());
            Assert.Equal("unknown", drawings.GetProperty("boundarySource").GetString());
            Assert.Equal(JsonValueKind.Null, drawings.GetProperty("pageSize").ValueKind);
            Assert.Equal(420, drawings.GetProperty("plotFrameSize")[0].GetInt32());
            JsonElement[] rows = [.. drawings.GetProperty("items").EnumerateArray()];
            Assert.InRange(rows.Length, 1, 200);
            Assert.All(rows, r => Assert.DoesNotContain('\u0007', r.GetProperty("properties").GetProperty("text").GetString()!));
            ids.AddRange(rows.Select(r => r.GetProperty("locator").GetProperty("oid").GetInt64()));
            cursor = drawings.GetProperty("nextCursor").GetString();
            Assert.Equal(cursor is not null, drawings.GetProperty("truncated").GetBoolean());
            Assert.True(++calls < 208);
        }
        while (cursor is not null);
        Assert.Equal(Enumerable.Range(1, 207).Select(i => (long)i), ids);
        Assert.All(bridge.Calls.Where(c => c.Operation != OperationNames.ProjectGet), c => Assert.Equal("X:/Line1", ((PageGetArgs)c.Args!).Project));
    }

    [Fact]
    public async Task Defaults_AndLimitChange_WalkFiftyThenOneThenTheRest()
    {
        FakeBridgeClient bridge = Bridge(52);
        JsonElement first = Data(await Read(bridge, format: "detailed")).GetProperty("drawings");
        Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        JsonElement second = Data(await Read(bridge, 1, first.GetProperty("nextCursor").GetString(), "detailed")).GetProperty("drawings");
        Assert.Equal(51, second.GetProperty("items")[0].GetProperty("locator").GetProperty("oid").GetInt64());
        JsonElement last = Data(await Read(bridge, 200, second.GetProperty("nextCursor").GetString(), "detailed"));
        Assert.Equal(52, last.GetProperty("drawings").GetProperty("items")[0].GetProperty("locator").GetProperty("oid").GetInt64());
        Assert.False(last.GetProperty("truncated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("hint").ValueKind);
    }

    [Fact]
    public async Task OversizedRow_IsAnActionableError_AndEmptyPageHasNoCursor()
    {
        Assert.True((await Read(Bridge(1, 50000))).IsError);
        JsonElement drawings = Data(await Read(Bridge(0))).GetProperty("drawings");
        Assert.Equal(0, drawings.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, drawings.GetProperty("nextCursor").ValueKind);
    }

    internal static FakeBridgeClient Bridge(int count, int textLength = 0, TimeProvider? clock = null)
    {
        var bridge = new FakeBridgeClient();
        PageDrawingDiscovery.For(bridge, clock ?? new ManualTime(DateTimeOffset.UnixEpoch));
        bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new OpenProject { Name = "Line1", Path = "X:/Line1" } };
        bridge.Handlers[OperationNames.PageGet] = _ => new PageGetData { Project = "Line1", Page = new PageDetail { Name = "P", Type = "Circuit" } };
        var geometry = new PageDrawingGeometry { Project = "X:/Line1", Page = "P", GridSize = 4, PlotFrameSize = [420, 297] };
        geometry.Placements = [.. Enumerable.Range(1, count).Select(i => new DrawingPlacement
        {
            Kind = "Text", Location = [0, 0], Bounds = [0, 0, 10, 10],
            Locator = new() { Project = geometry.Project, Page = "P", Type = 4, Oid = i, Digest = i.ToString("x64", CultureInfo.InvariantCulture) },
            Properties = textLength == 0 ? [] : new() { ["text"] = "en_US@" + new string('t', textLength) + "\u0007;" },
        })];
        bridge.Handlers[OperationNames.PageDrawingGet] = _ => geometry;
        return bridge;
    }

    internal static Task<CallToolResult> Read(FakeBridgeClient bridge, int limit = 50, string? cursor = null, string format = "concise") =>
        PageTools.PageGetAsync(bridge, "P", response_format: format, include_drawings: true, drawing_limit: limit, drawing_cursor: cursor, cancellationToken: TestContext.Current.CancellationToken);

    internal static JsonElement Data(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }
}
