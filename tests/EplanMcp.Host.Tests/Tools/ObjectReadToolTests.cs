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
                Locator = args.Locator,
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
    [InlineData("object_type")]
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
            case "object_type":
                input["object_type"] = "Terminal";
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

    [Fact]
    public async Task Find_NullCursor_ReadsTheFirstPage()
    {
        FakeBridgeClient bridge = Bridge(3);
        JsonElement result = PropertyGetToolTests.Data(await Read(bridge, new PropertySchemaPaging(TimeProvider.System), new JsonObject { ["object_type"] = "Page", ["limit"] = 2, ["cursor"] = null }));
        Assert.Equal(2, result.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("HyperLink", "link", "https://example.test")]
    [InlineData("QRCode", "text", "QR payload")]
    [InlineData("DimensionItem", "dimension_text", "60 mm")]
    [InlineData("DimensionCircle", "dimension_text", "12 mm")]
    [InlineData("PlaceHolder", "name", "Motor")]
    [InlineData("MacroBox", "macro_name", "Motor.ema")]
    public async Task Find_AnnotationsExposePositionAndKeyFieldsWithoutChangingLocators(string kind, string key, string value)
    {
        FakeBridgeClient bridge = Bridge(1);
        var annotation = new ObjectSummary
        {
            Locator = new() { Project = Project, Page = "=A/1", Type = 4, Oid = 2, Digest = new string('a', 64) },
            Location = [20, 24],
            Properties = new() { [key] = value },
        };
        bridge.Handlers[OperationNames.ObjectFind] = _ => new ObjectReadData { Objects = [annotation] };
        JsonElement result = PropertyGetToolTests.Data(await Read(bridge, new(TimeProvider.System), new() { ["object_type"] = kind }));
        JsonElement row = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal(2, row.GetProperty("locator").GetProperty("oid").GetInt64());
        Assert.Equal([20d, 24d], row.GetProperty("location").EnumerateArray().Select(p => p.GetDouble()).ToArray());
        Assert.Equal(value, row.GetProperty("properties").GetProperty(key).GetString());
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
        Assert.Equal(input["locator"]!.ToJsonString(), JsonNode.Parse(get.GetProperty("locator").GetRawText())!.ToJsonString());
        Assert.False(get.TryGetProperty("hint", out _));
        bridge.Handlers[OperationNames.ObjectFind] = _ => new ObjectReadData { Unsupported = "not_implemented" };
        JsonElement unsupported = PropertyGetToolTests.Data(await Read(bridge, new(TimeProvider.System), new() { ["object_type"] = "Arc" }));
        Assert.Equal("unsupported", unsupported.GetProperty("status").GetString());
        Assert.NotNull(unsupported.GetProperty("reason").GetString());
        Assert.Empty(unsupported.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Get_DrawingDigestReturnsCurrentLocatorAndHintWithinBudget()
    {
        FakeBridgeClient bridge = Bridge();
        var current = new DrawingLocator
        {
            Project = Project,
            Page = "=GAA+A1&EFS1/9912",
            Type = 17,
            Oid = 69845,
            Digest = new string('a', 64),
        };
        const string hint = "The locator's digest is not this object's identity digest (for example a drawing locator from eplan_page_get); the result's locator is the current object locator.";
        bridge.Handlers[OperationNames.ObjectGet] = value =>
        {
            var args = Assert.IsType<ObjectReadArgs>(value);
            Assert.Equal(new string('b', 64), args.Locator!.Digest);
            Assert.Equal((current.Page, current.Type, current.Oid), (args.Locator.Page, args.Locator.Type, args.Locator.Oid));
            return new ObjectReadData
            {
                Locator = current,
                Hint = hint,
                Properties = new()
                {
                    Items = [.. Enumerable.Range(0, 200).Select(index => new PropertyReadRow("value", new("text", text: new string('x', 500)))
                    {
                        Target = new(projectObject: true),
                        PropertyId = 20011,
                        Index = index,
                    })],
                },
            };
        };
        JsonNode supplied = JsonSerializer.SerializeToNode(current, HostJson.Options)!;
        supplied["digest"] = new string('b', 64);

        CallToolResult response = await Read(bridge, new(TimeProvider.System), new()
        {
            ["object_type"] = "Function",
            ["locator"] = supplied,
            ["limit"] = 200,
        }, true);

        JsonElement result = PropertyGetToolTests.Data(response);
        Assert.Equal(new string('a', 64), result.GetProperty("locator").GetProperty("digest").GetString());
        Assert.Equal(hint, result.GetProperty("hint").GetString());
        Assert.True(result.GetProperty("truncated").GetBoolean());
        Assert.InRange(result.GetProperty("items").GetArrayLength(), 1, 199);
        Assert.InRange(ResultShaper.ResultTokens(result.GetRawText()) + ResultShaper.EstimateTokens(((TextContentBlock)response.Content[0]).Text), 1, 5000);
    }

    [Theory]
    [InlineData("Terminal", "Function")]
    [InlineData("TerminalStrip", "TerminalStrip")]
    [InlineData("PLC", "Function")]
    [InlineData("Cable", "Cable")]
    [InlineData("PlaceHolder", "PlaceHolder")]
    [InlineData("LocationBox", "LocationBox")]
    [InlineData("InterruptionPoint", "InterruptionPoint")]
    [InlineData("PotentialDefinition", "PotentialDefinition")]
    [InlineData("BlackBox", "Function")]
    [InlineData("PLCTerminal", "Function")]
    [InlineData("MacroBox", "MacroBox")]
    public async Task AddedType_DiscoveryAndGetPreserveLocatorAndUseFormalDictionaryName(string type, string dictionary)
    {
        FakeBridgeClient bridge = Bridge(1);
        var entry = PropertyCatalog.Entries.First(e => e.ObjectType == dictionary && e.PropertyId.HasValue && e.ReadRefusal is null);
        bridge.Handlers[OperationNames.ObjectGet] = value =>
        {
            var args = Assert.IsType<ObjectReadArgs>(value);
            Assert.Equal(type, args.ObjectType);
            Assert.Equal(1, args.Locator!.Oid);
            return new ObjectReadData
            {
                Properties = new() { Items = [new("value", new("text", text: "nonempty")) { Target = new(locator: new() { Project = Project, Kind = type.ToLowerInvariant(), Type = args.Locator.Type, Oid = args.Locator.Oid }), PropertyId = entry.PropertyId!.Value, Language = args.Language }] },
            };
        };
        JsonElement found = PropertyGetToolTests.Data(await Read(bridge, new(TimeProvider.System), new() { ["object_type"] = type }));
        Assert.Equal("supported", found.GetProperty("status").GetString());
        JsonElement locator = Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("locator");
        JsonElement get = PropertyGetToolTests.Data(await Read(bridge, new(TimeProvider.System), new()
        {
            ["object_type"] = type,
            ["locator"] = JsonNode.Parse(locator.GetRawText()),
            ["language"] = "zh_CN",
        }, true));
        JsonElement row = Assert.Single(get.GetProperty("items").EnumerateArray());
        Assert.Equal((entry.PropertyName, "zh_CN", "nonempty"),
            (row.GetProperty("property_name").GetString(), row.GetProperty("language").GetString(), row.GetProperty("value").GetString()));
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
