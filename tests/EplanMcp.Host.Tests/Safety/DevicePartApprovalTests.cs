using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

[Trait("Size", "Small")]
public sealed class DevicePartApprovalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartLineNamesPartVariantAndDevice(bool chinese)
    {
        var change = new PlannedChange { Action = "add", Kind = "part", Name = "=GAA+A1-SF91", NewValue = "SIE.3SB3400-0B", Property = "1" };
        Assert.Equal("add part SIE.3SB3400-0B (variant 1) to =GAA+A1-SF91", ApprovalTexts.Change(change, chinese));
        Assert.Equal("add part SIE.3SB3400-0B (variant 1) to =GAA+A1-SF91", Assert.Single(WriteChangeLines.For(new WritePlan { Changes = [change] })));
    }
}
