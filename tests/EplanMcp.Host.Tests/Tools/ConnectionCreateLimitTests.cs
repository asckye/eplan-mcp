using System.Text.Json;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ConnectionCreateLimitTests
{
    private static readonly int[] _point = [8, 24];
    [Fact]
    public void Contract_EmptyDefinitionAndGenerateDefault_AreExplicit()
    {
        var items = ConnectionCreateSchema.Items();
        var definition = items["$defs"]!["d"]!.AsObject();
        Assert.False(definition.ContainsKey("anyOf"));
        Assert.True(definition["properties"]!.AsObject().ContainsKey("color"));
        var generate = typeof(ConnectionCreateTools).GetMethod(nameof(ConnectionCreateTools.CreateAsync))!.GetParameters().Single(p => p.Name == "generate");
        Assert.Equal(true, generate.DefaultValue);
        JsonElement schema = JsonSerializer.SerializeToElement(items);
        Assert.True(PropertyGetContractTests.Accepts(schema, JsonSerializer.SerializeToElement(new[] { new { kind = "definition_point", position = _point } }), schema));
        Assert.False(PropertyGetContractTests.Accepts(schema, JsonSerializer.SerializeToElement(new[] { new { kind = "definition_point" } }), schema));
    }

    [Fact]
    public void CableBatch_GetsTheLongerLimit_OtherItemsKeepTheWriteLimit()
    {
        JsonElement[] wires = JsonSerializer.SerializeToElement(new object[] { new { kind = "wire" }, new { kind = "t_node" } }).EnumerateArray().ToArray();
        JsonElement[] cables = JsonSerializer.SerializeToElement(new object[] { new { kind = "wire" }, new { kind = "cable_definition" } }).EnumerateArray().ToArray();
        JsonElement[] odd = JsonSerializer.SerializeToElement(new object[] { new { kind = 3 }, "cable_definition" }).EnumerateArray().ToArray();

        Assert.Equal(PageWriteTools.DefaultLimit, ConnectionCreateTools.Limit(wires));
        Assert.Equal(TimeSpan.FromSeconds(180), ConnectionCreateTools.Limit(cables));
        Assert.Equal(PageWriteTools.DefaultLimit, ConnectionCreateTools.Limit(odd));
    }
}
