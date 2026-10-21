using EplanMcp.Host.Safety;
using Microsoft.Extensions.DependencyInjection;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>Every row of ADR-0005's decision table, with both settings of M3 design Q5.</summary>
[Trait("Size", "Small")]
public sealed class PolicyEngineTests
{
    [Theory]
    [InlineData("Read", true, "Deny", "Allow")]
    [InlineData("Read", false, "Deny", "Allow")]
    [InlineData("Read", false, "Approve", "Allow")]
    [InlineData("Write", true, "Deny", "Allow")]
    [InlineData("Write", true, "Approve", "Allow")]
    [InlineData("Write", false, "Deny", "Deny")]
    [InlineData("Write", false, "Approve", "NeedsApproval")]
    [InlineData("Destructive", true, "Deny", "NeedsApproval")]
    [InlineData("Destructive", true, "Approve", "NeedsApproval")]
    [InlineData("Destructive", false, "Deny", "Deny")]
    [InlineData("Destructive", false, "Approve", "NeedsApproval")]
    [InlineData("Code", true, "Deny", "Deny")]
    [InlineData("Code", true, "Approve", "Deny")]
    [InlineData("Code", false, "Approve", "Deny")]
    [InlineData("Read", true, "Approve", "Allow")]
    [InlineData("Code", false, "Deny", "Deny")]
    public void Decide_FollowsTheDecisionTable(string tier, bool inScratch, string outside, string expected)
    {
        var config = new HostConfig(["C:\\Scratch"], Enum.Parse<OutsideScratchWrites>(outside));

        PolicyDecision decision = PolicyEngine.Decide(Enum.Parse<ToolTier>(tier), inScratch, config);

        Assert.Equal(expected, decision.Verdict.ToString());
        Assert.Equal(decision.Verdict == PolicyVerdict.Deny, decision.Message is not null);
    }

    [Fact]
    public void TheOnlyApprovalChannelInTheHost_IsTheEplanDialog()
    {
        // ADR-0005, ADR-0011: nothing in eplanmcp can approve a write by itself; a test channel lives only in tests.
        Type[] channels = [.. typeof(PolicyEngine).Assembly.GetTypes().Where(t => typeof(IApprovalChannel).IsAssignableFrom(t) && !t.IsInterface)];

        Assert.Equal([typeof(EplanDialogChannel)], channels);
    }

    [Fact]
    public void TheHostsServices_AskOnlyInTheEplanDialog()
    {
        // What serve composes: the Large tests' scripted channel is registered only by their own in-process Host.
        var services = new ServiceCollection();
        services.AddEplanMcp();

        Assert.Equal([typeof(EplanDialogChannel)], services.Where(d => d.ServiceType == typeof(IApprovalChannel)).Select(d => d.ImplementationType));
    }

    [Fact]
    public void Decide_DenialMessages_NameThePolicyButNoWayAround()
    {
        var config = new HostConfig(["C:\\Scratch"], OutsideScratchWrites.Deny);

        string outside = PolicyEngine.Decide(ToolTier.Write, inScratch: false, config).Message!;
        string code = PolicyEngine.Decide(ToolTier.Code, inScratch: true, config).Message!;

        // mcp-tool-design 6.3: POLICY_DENIED names the policy and never how to get around it.
        Assert.All([outside, code], m => Assert.Contains("safety policy", m, StringComparison.Ordinal));
        Assert.All([outside, code], m => Assert.DoesNotContain("config", m, StringComparison.OrdinalIgnoreCase));
    }
}
