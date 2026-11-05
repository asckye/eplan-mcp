using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class PlacementArrangementToolTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("左,0°", false)]
    [InlineData("左,0°", true)]
    public async Task DeviceArrangement_ReachesPlanAndExecute_AndOmissionPreservesDefault(string? arrangement, bool dry)
    {
        using var harness = new WriteToolHarness();
        harness.Bridge.Handlers[OperationNames.DevicePlacePlan] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "create", Kind = "device", Name = "/1" }]);
            plan.Details = new() { ["part"] = "P", ["tag"] = "-Q1" };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.DevicePlaceExecute] = _ => new DevicePlaceData
        { Project = "Line1", Page = "/1", PropertyArrangement = arrangement, ActivePropertySet = arrangement, Tag = "-Q1" };
        var call = await DeviceWriteTools.DevicePlaceAsync(harness.Pipeline, harness.Bridge, "Line1", "/1", 80, 160,
            part: "P", property_arrangement: arrangement, dry_run: dry, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement result = WriteToolHarness.Structured(call);
        Assert.Equal(dry, result.GetProperty("dryRun").GetBoolean());
        Assert.Equal(arrangement, ((DevicePlaceArgs)harness.Bridge.Calls.First(c => c.Operation == OperationNames.DevicePlacePlan).Args!).PropertyArrangement);
        if (!dry)
        { Assert.Equal(arrangement, ((DevicePlaceArgs)harness.Bridge.Calls.First(c => c.Operation == OperationNames.DevicePlaceExecute).Args!).PropertyArrangement); }
        if (dry || arrangement is null)
        { Assert.False(result.TryGetProperty("propertyArrangement", out _)); }
        else
        { Assert.Equal(arrangement, result.GetProperty("propertyArrangement").GetString()); Assert.Equal(arrangement, result.GetProperty("activePropertySet").GetString()); }
    }
}
