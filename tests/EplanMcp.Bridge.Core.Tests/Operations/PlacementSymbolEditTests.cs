using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementSymbolEditTests
{
    [Theory]
    [InlineData("BP", 4)]
    [InlineData("Q_3LI", 6)]
    public void ExistingSymbol_ChangesOnlyVariant_OneTransaction(string name, int variant)
    {
        var fake = Symbol(name);
        JObject request = fake.Input("set_variant");
        request["symbol_variant"] = variant;
        var reader = new Symbols(name);
        JObject preview = (JObject)new PlacementEditOperation(fake, fake, false, fake.Backup, reader).Execute(request)!;
        fake.Mutation = () => fake.Data["objects"]![0]!["symbol"]!["variant"] = variant;
        JObject result = (JObject)new PlacementEditExecuteOperation(fake, fake, fake.Backup, reader).Execute(new JObject { ["request"] = request, ["fingerprint"] = preview["fingerprint"] })!;
        Assert.Equal(variant, (int)result["predicted"]![0]!["symbol"]!["variant"]!);
        Assert.Equal([20d, 20d], result["predicted"]![0]!["location"]!.Values<double>());
        Assert.Contains("eplan_connection_generate", (string)result["warning"]!, StringComparison.Ordinal);
        Assert.Equal((1, 1), (fake.Transactions, fake.Closed));
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("line")]
    [InlineData("negative")]
    [InlineData("fraction")]
    [InlineData("cross_page")]
    [InlineData("extra")]
    public void InvalidSymbolEdit_IsRejectedBeforeTransaction(string scenario)
    {
        var fake = Symbol("BP");
        JObject request = fake.Input("set_variant");
        request["symbol_variant"] = 4;
        if (scenario == "missing")
        { request["symbol_variant"] = 7; }
        if (scenario == "line")
        { fake.Data["objects"]![0]!["symbol"] = null; }
        if (scenario == "negative")
        { request["symbol_variant"] = -1; }
        if (scenario == "fraction")
        { request["symbol_variant"] = 1.5; }
        if (scenario == "cross_page")
        { request["locators"]![0]!["page"] = "other"; }
        if (scenario == "extra")
        { request["library"] = "other"; }
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new PlacementEditOperation(fake, fake, false, fake.Backup, new Symbols("BP")).Execute(request));
        if (scenario == "missing")
        { Assert.Contains("0, 4, 6", error.Message, StringComparison.Ordinal); }
        Assert.Equal(0, fake.Transactions);
    }
    [Fact]
    public void IdenticalVariant_IsNoOp_AndMismatchRollsBack()
    {
        var fake = Symbol("BP");
        JObject request = fake.Input("set_variant");
        request["symbol_variant"] = 0;
        var reader = new Symbols("BP");
        JObject Plan() => (JObject)new PlacementEditOperation(fake, fake, false, fake.Backup, reader).Execute(request)!;
        JObject Run() => (JObject)new PlacementEditExecuteOperation(fake, fake, fake.Backup, reader).Execute(new JObject { ["request"] = request, ["fingerprint"] = Plan()["fingerprint"] })!;
        Assert.True((bool)Run()["noOp"]!);
        Assert.Equal(0, fake.Transactions);
        request["symbol_variant"] = 4;
        string baseline = fake.Data.ToString();
        fake.Mutation = () => { fake.Data["objects"]![0]!["symbol"]!["variant"] = 4; fake.Data["objects"]![0]!["location"]![0] = 21; };
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Run()).Error.Execution);
        Assert.Equal(baseline, fake.Data.ToString());
    }
    internal static PlacementEditFixture Symbol(string name)
    {
        var fake = new PlacementEditFixture();
        fake.Data["objects"]![0]!["kind"] = "SymbolReference";
        fake.Data["objects"]![0]!["symbol"] = new JObject { ["library"] = "IEC_symbol", ["name"] = name, ["variant"] = 0 };
        return fake;
    }
    private sealed class Symbols(string name) : ISymbolReader
    {
        private static readonly int[] _variants = [0, 4, 6];
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => [.. _variants.Select(v => new SymbolSearchItem { Library = "IEC_symbol", Name = name, Variant = v })];
    }
}
