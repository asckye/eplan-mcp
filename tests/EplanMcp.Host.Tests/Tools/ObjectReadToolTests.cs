using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ObjectReadToolTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Project = "X:/fixture/Line1";
    internal static FakeBridgeClient Bridge(int count = 5)
    {
        var bridge = new FakeBridgeClient { Status = new([new("fake-pipe", 7)], new() { Eplan = new() { ProcessId = 7 } }, null) };
        bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Name = "Line1", Path = Project } };
        bridge.Handlers[OperationNames.ObjectFind] = value =>
        {
            var args = (ObjectReadArgs)value!;
            int end = Math.Min(args.Offset + 3, count);
            return new ObjectReadData
            {
                Objects = [.. Enumerable.Range(args.Offset, end - args.Offset).Select(i => new ObjectSummary
            {
                Name = "object " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Locator = new() { Project = Project, Page = "=A/1", Type = 1, Oid = i + 1, Digest = new string('a', 64) },
            })],
                NextOffset = end < count ? end : null
            };
        };
        bridge.Handlers[OperationNames.ObjectGet] = value =>
        {
            var args = (ObjectReadArgs)value!;
            return new ObjectReadData
            {
                Properties = new()
                {
                    Items = [new PropertyReadRow("value", new("text", text: "text"))
            { Target = new(projectObject: true), PropertyId = 11011, Language = args.Language }]
                }
            };
        };
        return bridge;
    }
    internal static Task<CallToolResult> Read(FakeBridgeClient bridge, PropertySchemaPaging paging, JsonObject input, bool get = false) =>
        ObjectReadTools.ReadAsync(bridge, paging, get, JsonSerializer.SerializeToElement(input), Token);

    [Fact]
    public async Task Find_CursorPagesAcrossBlocks_WithoutLosingRowsOrChangingPayloadCopies()
    {
        FakeBridgeClient bridge = Bridge(17);
        var paging = new PropertySchemaPaging(TimeProvider.System);
        var input = new JsonObject { ["object_type"] = "Page", ["limit"] = 2 };
        var ids = new List<long>();
        do
        {
            JsonElement result = PropertyGetToolTests.Data(await Read(bridge, paging, input));
            ids.AddRange(result.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("locator").GetProperty("oid").GetInt64()));
            Assert.Equal(Project, result.GetProperty("project").GetString());
            input["cursor"] = result.GetProperty("cursor").GetString();
            Assert.Equal(input["cursor"] is not null, result.GetProperty("truncated").GetBoolean());
            Assert.InRange(ids.Count, 1, 17);
        } while (input["cursor"] is not null);
        Assert.Equal(Enumerable.Range(1, 17).Select(i => (long)i), ids);
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("content")]
    [InlineData("instance")]
    [InlineData("connection")]
    [InlineData("expired")]
    public async Task Cursor_RejectsChangedBindingContentSessionAndExpiry(string change)
    {
        FakeBridgeClient bridge = Bridge();
        var clock = new ManualTime(DateTimeOffset.UnixEpoch);
        var paging = new PropertySchemaPaging(clock);
        var input = new JsonObject { ["object_type"] = "Page", ["limit"] = 1 };
        input["cursor"] = PropertyGetToolTests.Data(await Read(bridge, paging, input)).GetProperty("cursor").GetString();
        switch (change)
        {
            case "filter":
                input["page"] = "=A/2";
                break;
            case "content":
                Func<object?, object> original = bridge.Handlers[OperationNames.ObjectFind];
                bridge.Handlers[OperationNames.ObjectFind] = a => { var block = (ObjectReadData)original(a); block.Objects[0].Name = "changed"; return block; };
                break;
            case "instance":
                bridge.Status = new([new("replacement-pipe", 8)], new() { Eplan = new() { ProcessId = 8 } }, null);
                break;
            case "connection":
                paging = new(clock);
                break;
            case "expired":
                clock.Advance(TimeSpan.FromMinutes(11));
                break;
        }
        JsonElement error = PropertyGetRequestTests.Error(await Read(bridge, paging, input));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("{\"object_type\":\"nope\",\"limit\":300,\"extra\":true}")]
    [InlineData("{\"object_type\":\"Page\",\"limit\":\"1\"}")]
    [InlineData("{\"object_type\":\"Page\",\"limit\":null}")]
    [InlineData("{\"object_type\":\"Page\",\"limit\":1.5}")]
    public async Task Validation_RejectsInvalidInputsBeforeConnecting(string json)
    {
        var bridge = new FakeBridgeClient();
        CallToolResult result = await ObjectReadTools.ReadAsync(bridge, new(TimeProvider.System), false, JsonSerializer.Deserialize<JsonElement>(json), Token);
        Assert.Equal(ErrorReason.InvalidArgument, PropertyGetRequestTests.Error(result).GetProperty("reason").GetString());
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task Get_MapsFormalNameAndLanguage_AndKeepsUnsupportedDistinctFromEmpty()
    {
        FakeBridgeClient bridge = Bridge();
        JsonElement found = PropertyGetToolTests.Data(await Read(bridge, new(TimeProvider.System), new() { ["object_type"] = "Page" }));
        var input = new JsonObject { ["object_type"] = "Page", ["locator"] = JsonNode.Parse(found.GetProperty("items")[0].GetProperty("locator").GetRawText()), ["language"] = "zh_CN" };
        JsonElement get = PropertyGetToolTests.Data(await Read(bridge, new(TimeProvider.System), input, true));
        JsonElement row = Assert.Single(get.GetProperty("items").EnumerateArray());
        Assert.Equal((11011, "PAGE_NOMINATIOMN", "zh_CN", "text"), (row.GetProperty("property_id").GetInt32(), row.GetProperty("property_name").GetString(), row.GetProperty("language").GetString(), row.GetProperty("value").GetString()));
        bridge.Handlers[OperationNames.ObjectFind] = _ => new ObjectReadData { Unsupported = "not_implemented_in_batch_1" };
        JsonElement unsupported = PropertyGetToolTests.Data(await Read(bridge, new(TimeProvider.System), new() { ["object_type"] = "Arc" }));
        Assert.Equal("unsupported", unsupported.GetProperty("status").GetString());
        Assert.NotNull(unsupported.GetProperty("reason").GetString());
        Assert.Empty(unsupported.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task BudgetPages_AndInvalidBridgeContinuationsAreRejected()
    {
        FakeBridgeClient bridge = Bridge(30);
        Func<object?, object> original = bridge.Handlers[OperationNames.ObjectFind];
        bridge.Handlers[OperationNames.ObjectFind] = a => { var block = (ObjectReadData)original(a); foreach (ObjectSummary row in block.Objects) { row.Name = new string('x', 500); } return block; };
        CallToolResult result = await Read(bridge, new(TimeProvider.System), new() { ["object_type"] = "Page", ["limit"] = 200 });
        JsonElement data = PropertyGetToolTests.Data(result);
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.InRange(ResultShaper.ResultTokens(data.GetRawText()) + ResultShaper.EstimateTokens(((TextContentBlock)result.Content[0]).Text), 1, 5000);
        bridge.Handlers[OperationNames.ObjectFind] = _ => new ObjectReadData { NextOffset = 0 };
        Assert.Equal(ErrorReason.InvalidArgument, PropertyGetRequestTests.Error(await Read(bridge, new(TimeProvider.System), new() { ["object_type"] = "Page" })).GetProperty("reason").GetString());
    }
}
