using EplanMcp.Contracts;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>The decision table of M6 design 4.4 (docs/tools/eplan_pack_get.md, section 3).</summary>
[Trait("Size", "Small")]
public sealed class PackCapabilitiesTests
{
    private static readonly HandshakeResult _eplan = new()
    {
        Eplan = new EplanInstanceInfo { Version = "2026.0.3.25421", Variant = "Electric P8" },
        Operations = ["page.list", "macro.insert.plan"],
    };

    private static readonly string[] _tools = ["eplan_macro_insert", "eplan_page_list"];

    [Theory]
    [InlineData(">=2026.0 <2027", "met")]
    [InlineData("2025.0", "missing")]
    public void EplanVersions_AreHardAndCompareWithTheConnectedEplan(string versions, string status)
    {
        CapabilityCheck check = Assert.Single(PackCapabilities.Check(new PackCompatibility { EplanVersions = versions }, _eplan, _tools));

        Assert.Equal((true, status), (check.Hard, check.Status));
    }

    [Theory]
    [InlineData("op:macro.insert.plan", true, "met")]
    [InlineData("op:module.insert", true, "missing")]
    [InlineData("eplan_macro_insert", true, "met")]
    [InlineData("eplan_plan_apply", true, "missing")]
    [InlineData("licence:CogineerBuilder", true, "unknown")]
    [InlineData("plugin:acme.terminal-allocation", true, "missing")]
    [InlineData("A:backup/PROJECT", false, "unchecked")]
    [InlineData("O:Eplan.EplApi.DataModel.Graphics.PlaceHolder.ApplyRecord", false, "unchecked")]
    public void Capabilities_ByPrefix(string capability, bool hard, string status)
    {
        CapabilityCheck check = Assert.Single(PackCapabilities.Check(new PackCompatibility { Capabilities = [capability] }, _eplan, _tools));

        Assert.Equal((hard, status), (check.Hard, check.Status));
    }

    [Fact]
    public void WithoutEplan_EplanRequirementsAreUnknown_AndTheResultTooUnlessOneIsMissing()
    {
        var compatibility = new PackCompatibility { EplanVersions = "2026.0", Variants = ["Pro Panel"], Capabilities = ["op:page.list", "A:backup/PROJECT"] };

        IReadOnlyList<CapabilityCheck> checks = PackCapabilities.Check(compatibility, null, _tools);

        Assert.Equal(["unknown", "unknown", "unknown", "unchecked"], checks.Select(c => c.Status));
        Assert.Null(PackCapabilities.AllMet(checks));
        Assert.False(PackCapabilities.AllMet([.. checks, new CapabilityCheck("eplan_plan_apply", true, PackCapabilities.Missing, null)]));
    }

    [Fact]
    public void Variants_AnyOfThemFits_AndWarningsNeverBlock()
    {
        IReadOnlyList<CapabilityCheck> checks = PackCapabilities.Check(new PackCompatibility { Variants = ["Pro Panel", "electric p8"], Capabilities = ["A:backup/PROJECT"] }, _eplan, _tools);

        Assert.Equal("met", checks[0].Status);
        Assert.True(PackCapabilities.AllMet(checks));
    }
}
