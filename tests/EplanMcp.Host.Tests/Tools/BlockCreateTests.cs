using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class BlockCreateTests
{
    private static JsonObject Item(int count = 3) => new()
    {
        ["kind"] = "block",
        ["name"] = "144_268557",
        ["members"] = JsonSerializer.SerializeToNode(Enumerable.Range(1, count).Select(i => new DrawingLocator { Project = "Line1", Page = "P", Type = 4, Oid = i, Digest = new string('a', 64) }), JsonSerializerOptions.Web)
    };

    [Theory]
    [InlineData(2, true)]
    [InlineData(1000, true)]
    [InlineData(1, false)]
    [InlineData(1001, false)]
    public void LimitsAgreeWithSchema(int count, bool valid)
    {
        JsonElement input = PlacementCreateTests.Input(Item(count).ToJsonString());
        Assert.Equal(valid, PlacementCreatePlanning.Validate(input).Count == 0);
        Assert.Equal(valid, PropertyGetContractTests.Accepts(PlacementCreateContract.Input, input, PlacementCreateContract.Input));
    }

    [Fact]
    public void PageGetCamelCaseLocatorsSurviveBridgeRequestMapping()
    {
        JsonElement item = JsonDocument.Parse("""{"kind":"block","members":[{"project":"P","page":"1","type":4,"oid":123,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},{"project":"P","page":"1","type":4,"oid":124,"digest":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}""").RootElement;
        PlacementText mapped = PlacementCreatePlanning.Request(item);
        Assert.Equal(("P", "1", 4, 123L, new string('a', 64)), (mapped.Members![0].Project, mapped.Members[0].Page, mapped.Members[0].Type, mapped.Members[0].Oid, mapped.Members[0].Digest));
    }

    [Fact]
    public void MixedBatchIsRejected()
    {
        Assert.NotEmpty(PlacementCreatePlanning.Validate(PlacementCreateTests.Input(Item().ToJsonString() + "," + PlacementCreateTests.Line)));
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("oid")]
    [InlineData("extra")]
    public void IncompleteOrUnknownLocatorIsRejected(string field)
    {
        JsonObject item = Item();
        JsonObject locator = item["members"]![0]!.AsObject();
        if (field == "extra")
        { locator[field] = true; }
        else
        { locator.Remove(field); }
        Assert.NotEmpty(PlacementCreatePlanning.Validate(PlacementCreateTests.Input(item.ToJsonString())));
    }

    [Fact]
    public async Task DefaultPreviewDoesNotSnapMemberCoordinatesOrWrite()
    {
        JsonObject input = JsonNode.Parse(PlacementCreateTests.Input(Item().ToJsonString()).GetRawText())!.AsObject();
        input.Remove("dry_run");
        var bridge = PlacementCreateTests.Bridge();
        JsonElement result = PlacementCreateTests.Data(await PlacementCreateTests.Preview(bridge, JsonSerializer.SerializeToElement(input)));
        Assert.Equal(ExecutionState.NotStarted, result.GetProperty("execution").GetString());
        Assert.Empty(bridge.Writes);
        Assert.Equal(3, result.GetProperty("items")[0].GetProperty("actual").GetProperty("count").GetInt32());
        Assert.Equal("144_268557", result.GetProperty("items")[0].GetProperty("actual").GetProperty("name").GetString());
    }
}
