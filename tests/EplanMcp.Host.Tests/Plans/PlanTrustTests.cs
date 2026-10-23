using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanTrustTests
{
    [Fact]
    public void Trust_CompilationDigestIsBoundToBothHostAndSession()
    {
        var trust = new PlanTrust();
        PlanDefinition definition = PlanTestData.Definition();
        string hash = trust.Register(definition);
        Assert.True(trust.Matches(definition, definition.Owner, hash));
        Assert.False(trust.Matches(definition, definition.Owner with { RunId = "other" }, hash));
        Assert.False(trust.Matches(definition, definition.Owner with { SessionId = "other" }, hash));
        Assert.False(trust.Matches(definition with { Owner = new("other", "session") }, new("other", "session"), hash));
        Assert.False(trust.Matches(definition with { PlanId = new string('f', 32) }, definition.Owner, hash));
        Assert.False(new PlanTrust().Matches(definition, definition.Owner, hash));
        Assert.Throws<InvalidOperationException>(() => trust.Register(definition));
    }

    [Fact]
    public void Trust_TamperingWithContentAndItsRecordedHash_DoesNotReplaceTrustedDigest()
    {
        var trust = new PlanTrust();
        PlanDefinition definition = PlanTestData.Definition();
        string hash = trust.Register(definition);
        PlanDefinition tampered = definition with { Project = definition.Project with { Name = "Changed" } };
        string forged = PlanCanonical.Hash(tampered);
        Assert.False(trust.Matches(tampered, definition.Owner, hash));
        Assert.False(trust.Matches(tampered, definition.Owner, forged));
        Assert.False(trust.Matches(definition, definition.Owner, forged));
        Assert.False(trust.Matches(definition, definition.Owner, hash.ToUpperInvariant()));
        Assert.Throws<InvalidOperationException>(() => trust.Register(tampered));
        Assert.True(trust.Matches(definition, definition.Owner, hash));
    }
}
