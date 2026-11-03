using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class DeviceUpdatePlanCompatibilityTests
{
    [Fact]
    public void PlanStepsContinueToOfferRenamingOnly()
    {
        var schema = PlanStepCatalog.ArgsSchema("device.update");
        var properties = schema["properties"]!.AsObject();
        Assert.False(properties.ContainsKey("part"));
        Assert.False(properties.ContainsKey("variant"));
        Assert.Equal("string", properties["new_tag"]!["type"]!.GetValue<string>());
        Assert.Contains("new_tag", schema["required"]!.AsArray().Select(item => item!.GetValue<string>()));
    }
}
