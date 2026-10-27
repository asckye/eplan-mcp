using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Host.Packs;

internal sealed partial class KnowledgeReviewService
{
    public async Task PublishAsync(string pack, KnowledgeReview review, string digest, CancellationToken cancellationToken)
    {
        if (review.TargetContent is null)
        { return; }
        await packs.AuditAsync(digest, "eplan_asset_review", "started", new JsonObject { ["action"] = "promote", ["reviewDigest"] = digest }, false, cancellationToken).ConfigureAwait(false);
        await packs.Dev.PromoteAsync(pack, KnowledgeDocument.Read(review.TargetContent), async () =>
        {
            if (!await VerifyAsync(digest, review, cancellationToken).ConfigureAwait(false))
            { throw Failure(ErrorReason.PlanStale, "/review: confirmation no longer matches; read and review again."); }
        }, PackLibrary.CreatedBy, packs.Clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await packs.AuditAsync(digest, "eplan_asset_review", "committed", new JsonObject { ["rule"] = review.TargetResource, ["digest"] = PlanStore.Hash(review.TargetContent) }, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task CheckBuildAsync(string folder, PackManifest manifest, CancellationToken cancellationToken)
    {
        foreach (AssetEntry entry in (await AssetIndex.ReadAsync(folder, manifest, cancellationToken).ConfigureAwait(false)).Where(e => e.Resource.Kind == ResourceKind.Rule || manifest.Rules.Any(s => s.Path == e.Resource.Path && s.Standing == KnowledgeStanding.Official)))
        {
            bool official = manifest.Rules.Any(s => s.Path == entry.Resource.Path && s.Standing == KnowledgeStanding.Official);
            if (!official && entry.Document?.Kind != KnowledgeKind.Rule)
            { continue; }
            if (entry.Resource.Kind != ResourceKind.Rule || entry.Document is not { Kind: KnowledgeKind.Rule, ApiVersion: KnowledgeDocument.V2ApiVersion })
            { throw Failure(ErrorReason.PackInvalid, "/rules: official content needs a reviewed v2 Rule; keep legacy material as a candidate."); }
            if (!official)
            { throw Failure(ErrorReason.PackInvalid, "/rules: unconfirmed Rule drafts must remain candidates; use eplan_asset_review."); }
            IReadOnlyList<JsonObject> records;
            try
            { records = await audit.ReadAsync(cancellationToken).ConfigureAwait(false); }
            catch (AuditUnavailableException)
            { throw Failure(ErrorReason.PackInvalid, "/rules: local confirmed reviews are unavailable; verify the audit log."); }
            JsonObject? record = records.LastOrDefault(r => (string?)r["kind"] == "knowledge_review" && (string?)r["targetResource"] == entry.Id && (string?)r["targetDigest"] == entry.Resource.Sha256);
            if (record is null)
            { throw Failure(ErrorReason.PackInvalid, "/rules: no matching local confirmed review digest; use eplan_asset_review to promote a candidate."); }
            var review = new KnowledgeReview((string)record["resource"]!, (string)record["contentDigest"]!, "promote", JsonSerializer.SerializeToElement(record["source"]), entry.Id,
                await File.ReadAllBytesAsync(Path.Combine(folder, entry.Resource.Path), cancellationToken).ConfigureAwait(false));
            if (PlanStore.Hash(review.TargetContent!) != entry.Resource.Sha256 || !await VerifyAsync((string)record["hash"]!, review, cancellationToken).ConfigureAwait(false)
                || !manifest.Resources.Any(r => AssetNames.Id(manifest.Id, AssetNames.Dev, AssetNames.Local(r)) == review.Resource && r.Sha256 == review.ContentDigest))
            { throw Failure(ErrorReason.PackInvalid, "/rules: reviewed content or source changed; read and review again."); }
        }
    }
}
