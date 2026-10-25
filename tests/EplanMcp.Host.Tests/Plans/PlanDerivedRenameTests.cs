using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanDerivedRenameTests
{
    [Theory]
    [InlineData("{\"type\":17,\"oid\":22212,\"function\":\"=F\\u002BA2-KF1\",\"pin\":\"A1\"}", "{\"type\":17,\"oid\":22212,\"function\":\"=F\\u002BA2-LP9\",\"pin\":\"A1\"}", true)]
    [InlineData("\"=F\\u002BA2-KF1:A1\"", "\"=F\\u002BA2-LP9:A1\"", true)]
    [InlineData("\"=F\\u002BA2-KF1:A1\"", "\"=F\\u002BA2-LP9:A2\"", false)]
    [InlineData("\"=F\\u002BA2-KF1:A1\"", "\"=F\\u002BA2-KF7:A1\"", false)]
    [InlineData("\"=F+A2-KF1:A1\"", "\"=F+A2-LP9:A1\"", false)]
    [InlineData("\"=F\\u002BA2-KE4\"", "\"=F\\u002BA2-KE4\"", false)]
    public void Reference_RequiresTheExactEscapedFullNameSubstitution(string from, string to, bool derived) =>
        Assert.Equal(derived, PlanDerivedRename.Reference(Step("device.update", "=F+A2-KF1", "=F+A2-LP9"), "end", from, to));

    [Theory]
    [InlineData("name")]
    [InlineData("exists")]
    public void Reference_NeverCoversIdentityProperties(string property) =>
        Assert.False(PlanDerivedRename.Reference(Step("device.update", "=F+A2-KF1", "=F+A2-LP9"), property, "\"=F\\u002BA2-KF1\"", "\"=F\\u002BA2-LP9\""));

    [Fact]
    public void Reference_OnlyForSingleDeviceRenames()
    {
        Assert.False(PlanDerivedRename.Reference(Step("device.renumber", "=F+A2-KF1", "=F+A2-LP9"), "end", "\"=F\\u002BA2-KF1\"", "\"=F\\u002BA2-LP9\""));
        Assert.False(PlanDerivedRename.Reference(Step("device.update", "=F+A2-KF1", null), "end", "\"=F\\u002BA2-KF1\"", "\"=F\\u002BA2-LP9\""));
    }

    [Theory]
    [InlineData("=F+A2-KF1", "=F+A2-LP9", "\"=F-KF1\"", "\"=F-LP9\"", true)]
    [InlineData("=F+A2-KF1", "=F+A2-LP9", "\"-KF1\"", "\"-LP9\"", true)]
    [InlineData("=F+A2-KF1", "=F+A2-LP9", "\"=F-KF1\"", "\"=G-LP9\"", false)]
    [InlineData("=F+A2-KF1", "=F+A3-LP9", "\"=F-KF1\"", "\"=F-LP9\"", false)]
    [InlineData("=F+A2-KF1", "=F+A2-LP9", "\"=F-KF10\"", "\"=F-LP90\"", false)]
    public void Visible_ReplacesOnlyTheTrailingDeviceIdentifierWhenTheStructureStays(string oldName, string newName, string from, string to, bool derived) =>
        Assert.Equal(derived, PlanDerivedRename.Visible(Step("device.update", oldName, newName), from, to));

    private static PlanStep Step(string op, string oldName, string? newName) => new(1, op, default, ToolTier.Write,
        PlanScopeRules.Pages([]), PlanScopeRules.Pages([]), [], [], [new("update", "function", oldName, "name", oldName, newName)], [], null, true);
}
