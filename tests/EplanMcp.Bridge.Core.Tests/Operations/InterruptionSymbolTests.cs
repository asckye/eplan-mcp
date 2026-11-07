using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class InterruptionSymbolTests
{
    [Theory]
    [InlineData("Left", 0, 60, 1)]
    [InlineData("Down", 7, 61, 0)]
    [InlineData("Right", 4, 60, 0)]
    [InlineData("Up", 3, 60, 1)]
    public void Bp_TailForOutgoingEnds_EAndHForIncomingEnds_AndPositionsMatchingPin(string direction, int variant, double y, int pin)
    {
        var fixture = new InterruptionPointFixture();
        fixture.Base.Symbols.Reverse();
        fixture.Request["items"]!.Last!["direction"] = direction;

        JToken row = Planned(fixture);

        Assert.Equal("BP", (string?)row["symbol"]!["name"]);
        Assert.Equal(variant, (int)row["symbol"]!["variant"]!);
        Assert.Equal([60d, y], row["position"]!.Values<double>());
        Assert.Equal(direction, (string?)row["symbol"]!["connectionPoints"]![pin]!["direction"]);
    }

    [Theory]
    [InlineData("BPIN", "Left", 0, 60, 60)]
    [InlineData("BPIN", "Down", 1, 60, 60)]
    [InlineData("BPIN", "Right", 2, 60, 60)]
    [InlineData("BPIN", "Up", 3, 60, 60)]
    [InlineData("BPOUT", "Left", 2, 64, 60)]
    [InlineData("BPOUT", "Down", 3, 60, 64)]
    [InlineData("BPOUT", "Right", 0, 56, 60)]
    [InlineData("BPOUT", "Up", 1, 60, 56)]
    [InlineData("BPF", "Left", 0, 60, 60)]
    [InlineData("BPF", "Down", 1, 60, 60)]
    [InlineData("BPF", "Right", 2, 60, 60)]
    [InlineData("BPF", "Up", 3, 60, 60)]
    public void SinglePointSymbols_PreserveDirectionAndOffsetChoices(string name, string direction, int variant, double x, double y)
    {
        var fixture = new InterruptionPointFixture();
        fixture.Base.Symbols.Clear();
        fixture.Base.Scene.Page.GridSize = 1;
        string[] directions = name == "BPOUT" ? ["Right", "Up", "Left", "Down", "Left", "Down", "Right", "Up"]
            : ["Left", "Down", "Right", "Up", "Right", "Up", "Left", "Down"];
        for (int i = (name == "BPF" ? 4 : 8) - 1; i >= 0; i--)
        {
            SymbolSearchItem symbol = ConnectionCreateFixture.Variant(name, "InterruptionPoint", i, directions[i]);
            if (name == "BPOUT")
            {
                symbol.ConnectionPoints[0].Location = directions[i] switch
                { "Right" => [4, 0], "Left" => [-4, 0], "Up" => [0, 4], _ => [0, -4] };
            }
            fixture.Base.Symbols.Add(symbol);
        }
        fixture.Request["items"] = new JArray(fixture.Request["items"]!.Last!.DeepClone());
        fixture.Request["items"]![0]!["symbol"] = name;
        fixture.Request["items"]![0]!["direction"] = direction;

        JToken row = Planned(fixture);

        Assert.Equal(name, (string?)row["symbol"]!["name"]);
        Assert.Equal(variant, (int)row["symbol"]!["variant"]!);
        Assert.Equal([x, y], row["position"]!.Values<double>());
    }

    [Fact]
    public void MultiPointSymbol_PrefersHighestMatchingIndex_ThenLowestVariant()
    {
        var fixture = new InterruptionPointFixture();
        SymbolSearchItem low = ConnectionCreateFixture.Variant("CUSTOM", "InterruptionPoint", 0, "Up", "Down");
        SymbolSearchItem tail = ConnectionCreateFixture.Variant("CUSTOM", "InterruptionPoint", 2, "Up", "Up", "Up");
        tail.ConnectionPoints[2].Location = [1, 2];
        SymbolSearchItem later = ConnectionCreateFixture.Variant("CUSTOM", "InterruptionPoint", 3, "Up", "Up", "Up");
        fixture.Base.Symbols.AddRange([later, low, tail]);
        fixture.Request["items"]!.Last!["symbol"] = "CUSTOM";

        JToken row = Planned(fixture);

        Assert.Equal(2, (int)row["symbol"]!["variant"]!);
        Assert.Equal([59d, 58d], row["position"]!.Values<double>());
    }

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("empty")]
    [InlineData("direction")]
    public void SymbolWithoutEligibleSpecialInterruptionVariant_IsNotSelected(string invalid)
    {
        var fixture = new InterruptionPointFixture();
        SymbolSearchItem symbol = ConnectionCreateFixture.Variant("CUSTOM", "InterruptionPoint", 0, "Up");
        switch (invalid)
        {
            case "library":
                symbol.Library = "OTHER";
                break;
            case "type":
                symbol.Type = "PotentialTerminal";
                break;
            case "empty":
                symbol.ConnectionPoints.Clear();
                break;
            case "direction":
                symbol.ConnectionPoints[0].Direction = "Down";
                break;
        }
        fixture.Base.Symbols.Add(symbol);
        fixture.Request["items"]!.Last!["symbol"] = "CUSTOM";

        Assert.Throws<BridgeErrorException>(() => fixture.Plan());
        Assert.Empty(fixture.Placement.Calls);
    }

    private static JToken Planned(InterruptionPointFixture fixture) => Assert.Single(fixture.Plan()["symbols"]!, row => row["interruption"] is not null);
}
