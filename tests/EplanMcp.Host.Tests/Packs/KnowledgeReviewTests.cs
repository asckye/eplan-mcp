using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Small")]
public sealed class KnowledgeReviewTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("digest")]
    [InlineData("identity")]
    [InlineData("project")]
    public void Bind_FakeSource_IsRejected(string change)
    {
        AssetEntry candidate = ReviewFixtures.Entry(ReviewFixtures.Document());
        AssetEntry? source = change switch
        {
            "missing" => null,
            "digest" => ReviewFixtures.Guide with { Resource = ReviewFixtures.Guide.Resource with { Sha256 = new string('f', 64) } },
            "identity" => ReviewFixtures.Guide with { Pack = "another" },
            _ => ReviewFixtures.Guide with { Resource = ReviewFixtures.Guide.Resource with { Files = 2 } },
        };

        BridgeCallException error = Assert.Throws<BridgeCallException>(() => KnowledgeReviewService.Bind(ReviewFixtures.Review(candidate), candidate, source));

        Assert.Equal(ErrorReason.PackInvalid, error.Error.Reason);
        Assert.StartsWith("/source/", error.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"engineer confirmed\"")]
    [InlineData("{\"reviewDigest\":\"confirmed\"}")]
    [InlineData("{\"asset\":\"document/guide.md\",\"location\":\"4.2\",\"hash\":\"wrong\"}")]
    [InlineData("{\"asset\":\"document/guide.md\",\"location\":7.5,\"hash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}")]
    [InlineData("{\"asset\":\"document/guide.md\",\"location\":\"4.2\",\"hash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"confirmed\":true}")]
    [InlineData("{\"asset\":\"document/guide.md\",\"location\":\"4.2\",\"hash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"hash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}")]
    public void Bind_ConfirmationTextOrInvalidSource_IsNotProof(string json)
    {
        AssetEntry candidate = ReviewFixtures.Entry(ReviewFixtures.Document());
        KnowledgeReview review = ReviewFixtures.Review(candidate) with { Source = JsonSerializer.Deserialize<JsonElement>(json) };

        Assert.Equal(ErrorReason.PackInvalid, Assert.Throws<BridgeCallException>(() => KnowledgeReviewService.Bind(review, candidate, ReviewFixtures.Guide)).Error.Reason);
    }

    [Fact]
    public void Bind_ReviewedContentChanges_RejectsTheOldDigest()
    {
        AssetEntry original = ReviewFixtures.Entry(ReviewFixtures.Document());
        KnowledgeDocument modified = ReviewFixtures.Document();
        modified.Spec["statement"] = "A different statement";
        AssetEntry changed = ReviewFixtures.Entry(modified);

        BridgeCallException error = Assert.Throws<BridgeCallException>(() => KnowledgeReviewService.Bind(ReviewFixtures.Review(original), changed, ReviewFixtures.Guide));

        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.StartsWith("/contentDigest:", error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_AcceptedCandidateAndOfficialStanding_NeverCreateARule()
    {
        KnowledgeDocument document = ReviewFixtures.Document();
        document.Spec["status"] = "accepted";
        AssetEntry candidate = ReviewFixtures.Entry(document) with { Standing = KnowledgeStanding.Official };
        byte[] before = PackJson.Serialize(document);

        JsonObject binding = KnowledgeReviewService.Bind(ReviewFixtures.Review(candidate), candidate, ReviewFixtures.Guide);

        Assert.Equal("accept", (string?)binding["decision"]);
        Assert.Null(binding["standing"]);
        Assert.Null(binding["targetDigest"]);
        Assert.Equal(before, PackJson.Serialize(document));
    }

    [Theory]
    [InlineData(KnowledgeKind.FactSet, "accept")]
    [InlineData(KnowledgeKind.Rule, "promote")]
    [InlineData(KnowledgeKind.Module, "accept")]
    [InlineData(KnowledgeKind.Candidate, "official")]
    [InlineData(KnowledgeKind.Candidate, "close_gap")]
    [InlineData(KnowledgeKind.Gap, "accept")]
    public void Bind_IncompatibleKindOrDecision_IsRejected(KnowledgeKind kind, string decision)
    {
        AssetEntry entry = ReviewFixtures.Entry(ReviewFixtures.Document(kind));

        Assert.Equal(ErrorReason.PackInvalid, Assert.Throws<BridgeCallException>(() => KnowledgeReviewService.Bind(ReviewFixtures.Review(entry, decision), entry, ReviewFixtures.Guide)).Error.Reason);
    }

    [Fact]
    public void Bind_Promotion_BindsTheNewRulesExactBytes()
    {
        AssetEntry candidate = ReviewFixtures.Entry(ReviewFixtures.Document());
        KnowledgeReview first = ReviewFixtures.Review(candidate, "promote");
        KnowledgeReview second = first with { TargetContent = [.. first.TargetContent!, (byte)'\n'] };

        JsonObject binding = KnowledgeReviewService.Bind(first, candidate, ReviewFixtures.Guide);

        Assert.Equal(first.TargetResource, (string?)binding["targetResource"]);
        Assert.Equal(PlanStore.Hash(first.TargetContent!), (string?)binding["targetDigest"]);
        Assert.NotEqual((string?)binding["targetDigest"], (string?)KnowledgeReviewService.Bind(second, candidate, ReviewFixtures.Guide)["targetDigest"]);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("kind")]
    [InlineData("target")]
    public void Bind_PromotionWithDifferentSourceOrIdentity_IsRejected(string change)
    {
        AssetEntry candidate = ReviewFixtures.Entry(ReviewFixtures.Document());
        KnowledgeReview review = ReviewFixtures.Review(candidate, "promote");
        KnowledgeDocument rule = KnowledgeDocument.Read(review.TargetContent!);
        if (change == "source")
        { rule.Spec["source"]!["location"] = "unreviewed"; }
        else if (change == "kind")
        { rule = rule with { Kind = KnowledgeKind.Candidate }; }
        review = review with { TargetContent = PackJson.Serialize(rule), TargetResource = change == "target" ? "acme.panels@dev:rule/another" : review.TargetResource };

        Assert.Equal(ErrorReason.PackInvalid, Assert.Throws<BridgeCallException>(() => KnowledgeReviewService.Bind(review, candidate, ReviewFixtures.Guide)).Error.Reason);
    }
}

internal static class ReviewFixtures
{
    internal static KnowledgeReviewService Service(PackLibrary packs) => new(packs, new Host.Audit.AuditLog(packs.Locations.Audit, packs.Clock),
        new Host.Safety.ApprovalService([], packs.Config, packs.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<Host.Safety.ApprovalService>.Instance), new Host.Tests.Tools.FakeBridgeClient());
    internal static readonly byte[] GuideBytes = Encoding.UTF8.GetBytes("# Engineering guide\n4.2 Motor starter requirements\n");
    internal static AssetEntry Guide => new("acme.panels", "dev", new PackResource
    {
        Path = "resources/docs/guide.md",
        Kind = ResourceKind.Document,
        Sha256 = PlanStore.Hash(GuideBytes),
        Bytes = GuideBytes.Length,
        Files = 1,
        Storage = ResourceStorage.Bundled,
    }, null, null, KnowledgeStanding.Candidate);

    internal static KnowledgeDocument Document(KnowledgeKind kind = KnowledgeKind.Candidate) => new()
    {
        ApiVersion = KnowledgeDocument.V2ApiVersion,
        Kind = kind,
        Metadata = new() { Name = "motor" },
        Spec = JsonNode.Parse(kind == KnowledgeKind.Gap ? """{"missing":"Starter evidence","status":"open"}"""
            : """{"statement":"Motors use starters","basis":["document/guide.md"],"confidence":"high","status":"proposed","inferredBy":"agent"}""")!.AsObject(),
    };

    internal static AssetEntry Entry(KnowledgeDocument document, string version = "dev") => new("acme.panels", version, new PackResource
    {
        Path = KnowledgeDocument.PathOf(document.Kind, document.Metadata.Name),
        Kind = KnowledgeDocument.ResourceKindOf(document.Kind),
        Sha256 = PlanStore.Hash(PackJson.Serialize(document)),
        Bytes = PackJson.Serialize(document).Length,
        Files = 1,
        Storage = ResourceStorage.Bundled,
    }, document, null, KnowledgeStanding.Candidate);

    internal static KnowledgeReview Review(AssetEntry entry, string decision = "accept")
    {
        JsonElement source = JsonSerializer.SerializeToElement(new { asset = "document/guide.md", location = "4.2", hash = Guide.Resource.Sha256 });
        KnowledgeDocument rule = Document(KnowledgeKind.Rule) with
        {
            Spec = JsonNode.Parse("""{"severity":"mandatory","scope":{"kind":"function","where":{"literal":true}},"check":{"exists":[{"property":"function.main"}]},"message":{"en":"A main function is required"}}""")!.AsObject(),
        };
        rule.Spec["source"] = JsonNode.Parse(source.GetRawText());
        return new(entry.Id, entry.Resource.Sha256, decision, source,
            decision == "promote" ? AssetNames.Id(entry.Pack, entry.Version, "rule/motor") : null, decision == "promote" ? PackJson.Serialize(rule) : null);
    }
}
