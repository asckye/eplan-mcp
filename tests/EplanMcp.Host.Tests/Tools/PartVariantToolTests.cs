using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class PartVariantToolTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PartVariantOnly_PassesBothPipelineStagesWithoutAddingASymbol(bool dry)
    {
        using var harness = new WriteToolHarness();
        harness.Bridge.Handlers[OperationNames.DevicePlacePlan] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "create", Kind = "device", Name = "/1" }]);
            plan.Details = new() { ["part"] = "PXC.3211813", ["tag"] = "-X1" };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.DevicePlaceExecute] = _ => new DevicePlaceData
        { Project = "Line1", Page = "/1", Part = "PXC.3211813", Tag = "-X1", SymbolVariant = 1 };
        var call = await DeviceWriteTools.DevicePlaceAsync(harness.Pipeline, harness.Bridge, "Line1", "/1", 80, 160,
            part: "PXC.3211813", symbol_variant: 1, dry_run: dry, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement result = WriteToolHarness.Structured(call);
        Assert.Equal(dry, result.GetProperty("dryRun").GetBoolean());
        foreach (var invocation in harness.Bridge.Calls.Where(c => c.Operation is OperationNames.DevicePlacePlan or OperationNames.DevicePlaceExecute))
        {
            var args = Assert.IsType<DevicePlaceArgs>(invocation.Args);
            Assert.Equal(1, args.SymbolVariant);
            Assert.Null(args.Library);
            Assert.Null(args.Symbol);
        }
        if (!dry)
        { Assert.Equal(1, result.GetProperty("symbolVariant").GetInt32()); }
        Assert.Equal(dry ? 0 : 1, harness.Bridge.Writes.Count);
    }
}
