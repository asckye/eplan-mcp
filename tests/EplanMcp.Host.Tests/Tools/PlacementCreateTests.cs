using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PlacementCreateTests
{
    internal const string Text = """{"kind":"text","position":[12.5,22.5],"contents":{"en_US":"Panel","de_DE":"Feld"},"height_mm":3}""";
    internal const string Line = """{"kind":"line","points":[[12.5,22.5],[32.5,22.5]]}""";
    internal const string Rectangle = """{"kind":"rectangle","corners":[[40,40],[10,10]]}""";

    [Fact]
    public void Contract_DeclaresBoundsClosedUnionAndWriteAnnotations()
    {
        Tool tool = PlacementCreateContract.Definition;
        Assert.Equal("eplan_placement_create", tool.Name);
        Assert.Equal((false, false, false, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint, tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
        Assert.Contains("project display language", tool.Description!, StringComparison.Ordinal);
        JsonElement input = tool.InputSchema;
        Assert.False(input.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["project", "page", "items"], input.GetProperty("required").EnumerateArray().Select(n => n.GetString()));
        JsonElement fields = input.GetProperty("properties");
        Assert.True(fields.GetProperty("snap_to_grid").GetProperty("default").GetBoolean());
        Assert.False(fields.GetProperty("dry_run").GetProperty("default").GetBoolean());
        Assert.Equal((1, 50), (fields.GetProperty("items").GetProperty("minItems").GetInt32(), fields.GetProperty("items").GetProperty("maxItems").GetInt32()));
        JsonElement[] branches = [.. input.GetProperty("$defs").GetProperty("item").GetProperty("oneOf").EnumerateArray()];
        Assert.Equal(["text", "line", "rectangle"], branches.Select(b => b.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()));
        Assert.All(branches, b => Assert.False(b.GetProperty("additionalProperties").GetBoolean()));
        JsonElement height = branches[0].GetProperty("properties").GetProperty("height_mm");
        Assert.Equal((0.5, 100), (height.GetProperty("minimum").GetDouble(), height.GetProperty("maximum").GetDouble()));
        Assert.True(JsonElement.DeepEquals(input.GetProperty("$defs"), tool.OutputSchema!.Value.GetProperty("$defs")));
    }

    [Theory]
    [InlineData("{}", "/kind")]
    [InlineData("null", "/items/0")]
    [InlineData("{\"kind\":\"wire\",\"corners\":[[1,2],[3,4]]}", "/kind")]
    [InlineData("{\"kind\":\"line\",\"points\":[[1,2],[3,4]],\"height_mm\":3}", "/height_mm")]
    [InlineData("{\"kind\":\"line\",\"kind\":\"line\",\"points\":[[1,2],[3,4]]}", "/kind")]
    [InlineData("{\"kind\":\"line\",\"points\":[[1e400,2],[3,4]]}", "/points/0/0")]
    [InlineData("{\"kind\":\"line\",\"points\":[[-1,2],[3,4]]}", "/points/0/0")]
    [InlineData("{\"kind\":\"line\",\"points\":[[10001,2],[3,4]]}", "/points/0/0")]
    [InlineData("{\"kind\":\"line\",\"points\":[[1,2,3],[3,4]]}", "/points/0")]
    [InlineData("{\"kind\":\"line\",\"points\":[[1,2]]}", "/points")]
    [InlineData("{\"kind\":\"rectangle\",\"points\":[[1,2],[3,4]]}", "/corners")]
    [InlineData("{\"kind\":\"text\",\"position\":[1,2],\"height_mm\":3,\"contents\":{\"en_US\":\"a\",\"en_US\":\"b\"}}", "/contents/en_US")]
    [InlineData("{\"kind\":\"text\",\"position\":[1,2],\"height_mm\":3,\"contents\":{\"en/us\":\"a\"}}", "/contents/en~1us")]
    public async Task InvalidItem_ReturnsPointerAndNeverReadsOrWrites(string item, string path)
    {
        var bridge = new FakeBridgeClient();
        JsonElement error = Error(await Preview(bridge, Input(item)));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains(path, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Empty(bridge.Calls);
        Assert.Empty(bridge.Writes);
    }

    [Theory]
    [InlineData("\"dry_run\":null")]
    [InlineData("\"snap_to_grid\":1")]
    [InlineData("\"response_format\":\"verbose\"")]
    [InlineData("\"page\":\"\"")]
    [InlineData("\"project\":null")]
    [InlineData("\"extra\":true")]
    public void InvalidRootOptions_AreRejected(string option)
    {
        using JsonDocument input = JsonDocument.Parse("{\"project\":\"Line1\",\"page\":\"P\",\"items\":[" + Line + "]," + option + "}");
        Assert.NotEmpty(PlacementCreatePlanning.Validate(input.RootElement));
    }

    [Fact]
    public void ItemAndPayloadLimits_AggregateViolations()
    {
        foreach (int count in new[] { 0, 51 })
        {
            JsonElement input = Input(string.Join(',', Enumerable.Repeat(Line, count)));
            Assert.Contains(PlacementCreatePlanning.Validate(input), v => v.StartsWith("/items:", StringComparison.Ordinal));
            Assert.False(PropertyGetContractTests.Accepts(PlacementCreateContract.Input, input, PlacementCreateContract.Input));
        }
        Assert.Empty(PlacementCreatePlanning.Validate(Input(string.Join(',', Enumerable.Repeat(Line, 50)))));
        JsonObject oversized = JsonNode.Parse(Input(Text).GetRawText())!.AsObject();
        oversized["items"]![0]!["contents"]!["en_US"] = new string('a', 256 * 1024);
        List<string> violations = PlacementCreatePlanning.Validate(JsonSerializer.SerializeToElement(oversized));
        Assert.Contains(violations, v => v.Contains("256 KiB", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith("/items/0/contents:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.49, false)]
    [InlineData(0.5, true)]
    [InlineData(100, true)]
    [InlineData(100.01, false)]
    public void TextHeight_UsesInclusiveLimits(double height, bool valid)
    {
        JsonObject input = JsonNode.Parse(Input(Text).GetRawText())!.AsObject();
        input["items"]![0]!["height_mm"] = height;
        JsonElement element = JsonSerializer.SerializeToElement(input);
        Assert.Equal(valid, PlacementCreatePlanning.Validate(element).Count == 0);
        Assert.Equal(valid, PropertyGetContractTests.Accepts(PlacementCreateContract.Input, element, PlacementCreateContract.Input));
    }

    [Theory]
    [InlineData("en_US", "en_US", null)]
    [InlineData("??_??", null, null)]
    [InlineData("en_US", "zh_CN", ErrorReason.InvalidArgument)]
    [InlineData("en_US", null, ErrorReason.CapabilityMissing)]
    public async Task TextLanguage_RequiresDisplayLanguageOrIndependentContent(string stored, string? display, string? reason)
    {
        JsonObject input = JsonNode.Parse(Input(Text).GetRawText())!.AsObject();
        input["items"]![0]!["contents"] = new JsonObject { [stored] = "Panel" };
        CallToolResult result = await Preview(Bridge(), JsonSerializer.SerializeToElement(input), display);
        if (reason is null)
        {
            JsonElement data = Data(result);
            Assert.Equal("Panel", data.GetProperty("items")[0].GetProperty("actual").GetProperty("contents").GetProperty(stored).GetString());
        }
        else
        { Assert.Equal(reason, Error(result).GetProperty("reason").GetString()); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"en_US\":\"\"}")]
    [InlineData("{\"en_US\":null}")]
    [InlineData("{\"EN_us\":\"a\"}")]
    [InlineData("{\"en_US\\n\":\"a\"}")]
    [InlineData("{\"default\":\"a\"}")]
    public void InvalidLanguageContents_AreRejected(string contents)
    {
        JsonObject input = JsonNode.Parse(Input(Text).GetRawText())!.AsObject();
        input["items"]![0]!["contents"] = JsonNode.Parse(contents);
        Assert.NotEmpty(PlacementCreatePlanning.Validate(JsonSerializer.SerializeToElement(input)));
    }

    [Theory]
    [InlineData(true, 15, 25)]
    [InlineData(false, 12.5, 22.5)]
    public async Task GridSnapping_ReportsRequestedAndActualWithoutChangingTextHeight(bool snap, double x, double y)
    {
        JsonElement data = Data(await Preview(Bridge(), Input(Text, snap: snap)));
        JsonElement item = data.GetProperty("items")[0];
        Assert.Equal(snap, item.GetProperty("snapped").GetBoolean());
        Assert.Equal(12.5, item.GetProperty("requested").GetProperty("position")[0].GetDouble());
        Assert.Equal(x, item.GetProperty("actual").GetProperty("position")[0].GetDouble());
        Assert.Equal(y, item.GetProperty("actual").GetProperty("position")[1].GetDouble());
        Assert.Equal(3, item.GetProperty("actual").GetProperty("height_mm").GetDouble());
        Assert.Equal(2, item.GetProperty("actual").GetProperty("contents").EnumerateObject().Count());
        Assert.True(PropertyGetContractTests.Accepts(PlacementCreateContract.Definition.OutputSchema!.Value, data, PlacementCreateContract.Definition.OutputSchema.Value));
    }

    [Fact]
    public async Task Bounds_RejectsFullTextOutlineEvenWithInsertionInsidePage()
    {
        CallToolResult result = await PlacementCreatePlanning.PreviewAsync(Bridge(), Input(Text), "en_US", _ => [15, 25, 101, 30], TestContext.Current.CancellationToken);
        Assert.Contains("/items/0:", Error(result).GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("line", "points")]
    [InlineData("rectangle", "corners")]
    public async Task Bounds_ChecksBothPointsAndUsesSortedOutline(string kind, string field)
    {
        string item = "{\"kind\":\"" + kind + "\",\"" + field + "\":[[103,30],[10,10]]}";
        Assert.Equal(ErrorReason.InvalidArgument, Error(await Preview(Bridge(), Input(item))).GetProperty("reason").GetString());
        JsonElement data = Data(await Preview(Bridge(), Input(Rectangle)));
        Assert.Equal([10, 10, 40, 40], data.GetProperty("items")[0].GetProperty("bounds").EnumerateArray().Select(n => n.GetInt32()));
    }

    [Fact]
    public async Task Bounds_AfterSnappingCanExceedPageAndCannotUsePlotFrameFallback()
    {
        FakeBridgeClient bridge = Bridge(size: [99, 99]);
        JsonElement error = Error(await Preview(bridge, Input("""{"kind":"line","points":[[10,10],[98,98]]}""")));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        bridge = Bridge();
        bridge.Handlers[OperationNames.PageDrawingGet] = _ => new PageDrawingGeometry { Project = "Line1", Page = "P", GridSize = 5, PlotFrameSize = [420, 297] };
        Assert.Equal("placement.page_geometry", Error(await Preview(bridge, Input(Line))).GetProperty("metadata").GetProperty("missing").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.Epsilon)]
    public async Task InvalidOrOverflowingGrid_FailsWithoutThrowing(double grid)
    {
        Assert.True((await Preview(Bridge(grid: grid), Input(Line))).IsError);
    }

    [Fact]
    public async Task TextOutline_UnknownOrInvalidFailsClosed()
    {
        foreach (double[]? outline in new double[]?[] { null, [], [1, 2, double.NaN, 4], [20, 20, 10, 10] })
        {
            JsonElement error = Error(await PlacementCreatePlanning.PreviewAsync(Bridge(), Input(Text), "en_US", _ => outline, TestContext.Current.CancellationToken));
            Assert.Equal("placement.text_outline", error.GetProperty("metadata").GetProperty("missing").GetString());
        }
    }

    [Fact]
    public async Task ExecutionAndBadPageType_AreClosedWithoutWrites()
    {
        var bridge = new FakeBridgeClient();
        Assert.Equal("placement.create.execute", Error(await Preview(bridge, Input(Line, dry: false))).GetProperty("metadata").GetProperty("missing").GetString());
        Assert.Empty(bridge.Calls);
        bridge = Bridge();
        bridge.Handlers[OperationNames.PageGet] = _ => new PageGetData { Page = new() { Name = "P", Type = "Overview" } };
        Assert.Equal(ErrorReason.InvalidArgument, Error(await Preview(bridge, Input(Line))).GetProperty("reason").GetString());
        Assert.Empty(bridge.Writes);
        Assert.DoesNotContain(bridge.Calls, c => c.Operation == OperationNames.PageDrawingGet);
    }

    [Fact]
    public async Task ReadFailure_PreservesReasonAndReportsNotStarted()
    {
        var bridge = new FakeBridgeClient { Failure = new BridgeError { Reason = ErrorReason.PageNotFound, Message = "Use eplan_page_list." } };
        Assert.Equal(ErrorReason.PageNotFound, Error(await Preview(bridge, Input(Line))).GetProperty("reason").GetString());
        Assert.Empty(bridge.Writes);
    }

    [Fact]
    public async Task Budget_RejectsInsteadOfTruncatingImpactList()
    {
        JsonElement input = Input(string.Join(',', Enumerable.Repeat(Text, 50)));
        Assert.Equal(ErrorReason.InvalidArgument, Error(await Preview(Bridge(), input)).GetProperty("reason").GetString());
        JsonObject detailed = JsonNode.Parse(input.GetRawText())!.AsObject();
        detailed["response_format"] = "detailed";
        Assert.Equal(50, Data(await Preview(Bridge(), JsonSerializer.SerializeToElement(detailed))).GetProperty("changes").GetArrayLength());
    }

    internal static JsonElement Input(string items, bool snap = true, bool dry = true) => JsonDocument.Parse(
        "{\"project\":\"Line1\",\"page\":\"P\",\"items\":[" + items + "],\"snap_to_grid\":" + (snap ? "true" : "false") + ",\"dry_run\":" + (dry ? "true" : "false") + "}").RootElement.Clone();

    internal static FakeBridgeClient Bridge(double[]? size = null, double grid = 5)
    {
        var bridge = new FakeBridgeClient();
        bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Path = "Line1", Name = "Line1" } };
        bridge.Handlers[OperationNames.PageGet] = _ => new PageGetData { Project = "Line1", Page = new() { Name = "P", Type = "Circuit" } };
        bridge.Handlers[OperationNames.PageDrawingGet] = _ => new PageDrawingGeometry { Project = "Line1", Page = "P", GridSize = grid, PageSize = size ?? [100, 100] };
        return bridge;
    }

    internal static Task<CallToolResult> Preview(FakeBridgeClient bridge, JsonElement input, string? language = "en_US") =>
        PlacementCreatePlanning.PreviewAsync(bridge, input, language, text =>
        {
            JsonElement p = text.GetProperty("position");
            double x = p[0].GetDouble(), y = p[1].GetDouble();
            return [x, y, x + 20, y + 5];
        }, TestContext.Current.CancellationToken);

    internal static JsonElement Data(CallToolResult result)
    {
        Assert.False(result.IsError == true);
        return result.StructuredContent!.Value;
    }

    internal static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        JsonElement error = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text).RootElement.GetProperty("error").Clone();
        Assert.Equal(ExecutionState.NotStarted, error.GetProperty("execution").GetString());
        return error;
    }
}
