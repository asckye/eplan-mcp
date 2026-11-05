using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Eplan.Adapters;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Fake placements and pages belong to the project.

[Trait("Size", "Small")]
public sealed class InterruptionVisibleNameTests
{
    [Theory]
    [InlineData("=GAA-2L1", "-2L1", true)]
    [InlineData("=GAA-2Z3", "-2Z3", true)]
    [InlineData("=GAA-2Z3", "", false)]
    public void FullNameAssignmentAdjustsVisibleName_AndFalseIsNotAnError(string name, string visible, bool adjusted)
    {
        var page = new Page();
        var point = new Function { VisibleName = "" };
        int calls = 0;
        NameService.Adjust = (target, function) =>
        {
            calls++;
            Assert.Same(page, target);
            Assert.Same(point, function);
            Assert.Equal(name, function.Name);
            point.VisibleName = visible;
            return adjusted;
        };
        try
        {
            EplanConnectionCreator.NameInterruption(page, point, name);
            Assert.Equal((name, visible, 1), (point.Name, point.VisibleName, calls));
        }
        finally { NameService.Adjust = null; }
    }

    [Fact]
    public void NativeAdjustmentExceptionIsNotSwallowed()
    {
        NameService.Adjust = (_, _) => throw new InvalidOperationException("native failure");
        try
        {
            Assert.Throws<InvalidOperationException>(() => EplanConnectionCreator.NameInterruption(new Page(), new Function(), "=GAA-2Z3"));
        }
        finally { NameService.Adjust = null; }
    }
}
