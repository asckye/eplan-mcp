using System.Text.Json;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ConnectionCreateLimitTests
{
    [Fact]
    public void Contract_EmptyDefinitionAndGenerateDefault_AreExplicit()
    {
        var generate = typeof(ConnectionCreateTools).GetMethod(nameof(ConnectionCreateTools.CreateAsync))!.GetParameters().Single(p => p.Name == "generate");
        Assert.Equal(true, generate.DefaultValue);
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
