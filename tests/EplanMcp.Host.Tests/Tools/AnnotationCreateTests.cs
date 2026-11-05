using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class AnnotationCreateTests
{
    public static TheoryData<string> Items =>
    [
        """{"kind":"hyperlink","position":[12.5,22.5],"link":"https://example.test","contents":{"??_??":"Reference"}}""",
        """{"kind":"qr_code","position":[12.5,22.5],"size_mm":5,"text":"Panel"}""",
        """{"kind":"spline","points":[[12.5,22.5],[30,40]]}""",
        """{"kind":"sector","center":[30,40],"radius_mm":5,"start_angle":10,"end_angle":120}""",
        """{"kind":"dimension","dimension_type":"linear","first":[12.5,22.5],"second":[30,40],"end":[30,50]}""",
        """{"kind":"dimension","dimension_type":"angular","first":[12.5,22.5],"second":[30,40],"end":[30,50]}""",
        """{"kind":"dimension","dimension_type":"baseline","points":[[12.5,22.5],[30,40]],"end":[30,50]}""",
        """{"kind":"dimension","dimension_type":"incremental","points":[[12.5,22.5],[30,40]],"end":[30,50]}""",
        """{"kind":"dimension","dimension_type":"continued","points":[[12.5,22.5],[30,40]],"end":[30,50]}""",
        """{"kind":"dimension","dimension_type":"circle","arc":{"project":"P","page":"1","type":1,"oid":2,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"end":[30,50]}""",
    ];

    [Theory]
    [MemberData(nameof(Items))]
    public async Task EachKind_DefaultsToReadOnlyPreviewAndUsesSharedStyle(string item)
    {
        var value = JsonNode.Parse(item)!;
        bool styled = value["kind"]!.GetValue<string>() is "hyperlink" or "qr_code" or "spline" or "sector";
        if (styled)
        {
            value["layer"] = 7;
            value["pen"] = new JsonObject { ["color"] = -16002, ["line_end"] = 2 };
        }
        JsonElement input = Input(value.ToJsonString());
        Assert.Empty(PlacementCreatePlanning.Validate(input, annotations: true));
        var bridge = PlacementCreateTests.Bridge();
        CallToolResult preview = await PlacementCreatePlanning.PreviewAsync(bridge, input, measureText: _ => null, annotations: true, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement data = PlacementCreateTests.Data(preview);
        Assert.Equal(ExecutionState.NotStarted, data.GetProperty("execution").GetString());
        Assert.True(data.GetProperty("dryRun").GetBoolean());
        Assert.Empty(bridge.Writes);
        PlacementText actual = PlacementCreatePlanning.Request(data.GetProperty("items")[0].GetProperty("actual"));
        if (styled)
        { Assert.Equal(7, actual.Layer); Assert.Equal(2, actual.Pen!["line_end"]); }
        else
        { Assert.Null(actual.Layer); Assert.Null(actual.Pen); }
        Assert.True(PropertyGetContractTests.Accepts(AnnotationCreateContract.Definition.OutputSchema!.Value, data, AnnotationCreateContract.Definition.OutputSchema.Value));
    }

    [Theory]
    [InlineData("""{"kind":"comment","position":[10,10]}""", "/kind")]
    [InlineData("""{"kind":"spline","points":[[1,2]]}""", "/points")]
    [InlineData("""{"kind":"qr_code","position":[1,2],"size_mm":0,"text":"x"}""", "/size_mm")]
    [InlineData("""{"kind":"hyperlink","position":[1,2],"link":1,"contents":{"??_??":"x"}}""", "/link")]
    [InlineData("""{"kind":"sector","center":[1,2],"radius_mm":-1,"start_angle":0,"end_angle":90}""", "/radius_mm")]
    [InlineData("""{"kind":"dimension","dimension_type":"angular","first":[1,2],"second":[1,2],"end":[2,3]}""", "/items/0")]
    [InlineData("""{"kind":"dimension","dimension_type":"linear","first":[1,2],"second":[2,3],"end":[3,4],"dimension_text":"5 mm"}""", "/dimension_text")]
    [InlineData("""{"kind":"dimension","dimension_type":"continued","points":"bad","end":[3,4]}""", "/points")]
    [InlineData("""{"kind":"dimension","dimension_type":"circle","arc":{},"end":[3,4]}""", "/arc")]
    [InlineData("""{"kind":"dimension","dimension_type":"linear","first":[1,2],"second":[2,3],"end":[3,4],"layer":7}""", "/layer")]
    public async Task InvalidInput_ReturnsPointerBeforeReadingOrWriting(string item, string path)
    {
        var bridge = new FakeBridgeClient();
        JsonElement error = PlacementCreateTests.Error(await PlacementCreatePlanning.PreviewAsync(bridge, Input(item), annotations: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains(path, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Empty(bridge.Calls);
        Assert.Empty(bridge.Writes);
    }

    [Fact]
    public async Task MissingGeometry_WarnsWithoutRefusalOrFabricatedBounds()
    {
        var bridge = PlacementCreateTests.Bridge(grid: 0);
        bridge.Handlers[OperationNames.PageDrawingGet] = _ => new PageDrawingGeometry { Project = "Line1", Page = "P" };
        JsonElement data = PlacementCreateTests.Data(await PlacementCreatePlanning.PreviewAsync(bridge, Input("""{"kind":"hyperlink","position":[12.5,22.5],"link":"https://example.test","contents":{"??_??":"Reference"}}"""), measureText: _ => null, annotations: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("gridSize").ValueKind);
        Assert.Equal("unverified", data.GetProperty("boundarySource").GetString());
        Assert.Contains("unreadable", data.GetProperty("warning").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("items")[0].GetProperty("bounds").ValueKind);
    }

    [Fact]
    public void Contract_PreservesBudgetsAndFullProfile()
    {
        Tool tool = AnnotationCreateContract.Definition;
        Assert.InRange(tool.InputSchema.GetRawText().Length, 1, 1000);
        Assert.InRange(tool.Description!.Length + PageDisplay.Description.Length, 1, 1000);
        Assert.False(ToolProfiles.Core.Contains(tool.Name));
        Assert.Equal((false, false, false, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint, tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
        Assert.Equal(AnnotationCreatePlanning.Kinds, tool.InputSchema.GetProperty("properties").GetProperty("items").GetProperty("items").GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    internal static JsonElement Input(string item) => JsonDocument.Parse("{\"project\":\"Line1\",\"page\":\"P\",\"items\":[" + item + "]}").RootElement.Clone();
}
