using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class PotentialEngineeringToolTests
{
    private static readonly int[] _position = [40, 40];
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TypedEngineeringFields_SurvivePreviewAndExecute(bool? dry)
    {
        using var harness = new WriteToolHarness();
        const string data = """{"project":"Line1","page":"page","undoSteps":1,"definitionPoints":[{"type":"PotentialDefinition","properties":{"potential_name":"N","potential_value":"N","potential_type":2,"color":"BU","cross_section":"1.5"}}]}""";
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "create", Kind = "PotentialDefinition", Name = "N" }]);
            plan.Details = new() { ["preview"] = data };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = _ => JsonNode.Parse(data)!;
        JsonElement item = JsonSerializer.SerializeToElement(new { kind = "potential_definition", position = _position, potential_name = "N", potential_value = "N", potential_type = 2, color = "BU", cross_section = "1.5" });
        JsonElement schema = JsonSerializer.SerializeToElement(ConnectionCreateSchema.Items());
        Assert.True(PropertyGetContractTests.Accepts(schema, JsonSerializer.SerializeToElement(new[] { item }), schema));
        var call = dry is null
            ? await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [item], cancellationToken: TestContext.Current.CancellationToken)
            : await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", "page", [item], dry_run: dry.Value, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement result = WriteToolHarness.Structured(call);
        Assert.Equal(dry != false, result.GetProperty("dryRun").GetBoolean());
        Assert.Equal(2, result.GetProperty("definitionPoints")[0].GetProperty("properties").GetProperty("potential_type").GetInt32());
        Assert.Equal(dry == false ? 1 : 0, harness.Bridge.Writes.Count);
        if (dry == false)
        { Assert.Equal(item.GetRawText(), JsonSerializer.SerializeToElement(harness.Bridge.Calls.Last().Args).GetProperty("request").GetProperty("items")[0].GetRawText()); }
    }
}
