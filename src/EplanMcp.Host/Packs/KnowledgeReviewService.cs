using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;
using ModelContextProtocol;

namespace EplanMcp.Host.Packs;

/// <summary>Stateless reviews: only the local, flushed audit grants confirmation; packs and standing strings grant none.</summary>
internal sealed partial class KnowledgeReviewService(PackLibrary packs, AuditLog audit, ApprovalService approvals, IBridgeClient bridge)
{
    internal Func<string, RuleValueType?>? Configuration { get; init; }

    public async Task<string> ReviewAsync(KnowledgeReview review, string tool, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        review = review with { Source = review.Source.Clone(), TargetContent = review.TargetContent?.ToArray() };
        JsonObject binding = await PrepareAsync(review, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<JsonObject> history;
        try
        { history = await audit.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (AuditUnavailableException)
        { throw Failure(ErrorReason.AuditUnavailable, "/review: audit unavailable; verify the audit log and review again."); }
        foreach (JsonObject record in history.Reverse())
        {
            if ((string?)record["kind"] == "knowledge_review" && binding.All(m => JsonNode.DeepEquals(m.Value, record[m.Key]))
                && (string?)record["hash"] is { } digest && await VerifyAsync(digest, review, cancellationToken).ConfigureAwait(false))
            { return digest; }
        }
        string fingerprint = PlanStore.Hash(PackJson.Serialize(binding));
        EplanInstanceInfo? eplan = (await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false)).Handshake?.Eplan;
        if (eplan is null)
        { throw Failure(ErrorReason.ApprovalDenied, "/review: connect an engineer's approval channel and review again."); }
        string requestId = Guid.NewGuid().ToString("N");
        var plan = new WritePlan
        {
            Fingerprint = fingerprint,
            Changes = [new PlannedChange { Action = "update", Kind = "knowledge review", Name = review.Resource, Property = "decision", OldValue = review.ContentDigest, NewValue = review.Decision },
                .. binding["source"]!.AsObject().Select(member => new PlannedChange { Action = "update", Kind = "knowledge review", Name = "source", Property = member.Key, NewValue = member.Value!.GetValue<string>() }),
                .. review.TargetResource is { } target ? new[] { new PlannedChange { Action = "create", Kind = "rule", Name = target, NewValue = binding["targetDigest"]!.GetValue<string>() } } : []],
        };
        ApprovalResult answer = await approvals.AskAsync(requestId, tool, ToolTier.Write, fingerprint, plan, eplan, null, null, progress, cancellationToken).ConfigureAwait(false);
        answer.Outcome?.Dispose();
        if (answer.Decision != ApprovalDecision.Granted)
        { throw new BridgeCallException(approvals.Refusal(answer.Decision)); }
        JsonObject current = await PrepareAsync(review, cancellationToken).ConfigureAwait(false);
        if (!JsonNode.DeepEquals(binding, current))
        { throw Failure(ErrorReason.PlanStale, "/review: reviewed content changed during confirmation; review again."); }
        binding["kind"] = "knowledge_review";
        binding["requestId"] = requestId;
        binding["tool"] = tool;
        binding["confirmation"] = "Granted";
        binding["channel"] = answer.Channel;
        try
        {
            return await audit.AppendAsync(binding, cancellationToken).ConfigureAwait(false);
        }
        catch (AuditUnavailableException)
        { throw Failure(ErrorReason.AuditUnavailable, "/review: no decision was recorded; run 'eplanmcp audit verify' and review again."); }
    }

    public async Task<bool> VerifyAsync(string reviewDigest, KnowledgeReview review, CancellationToken cancellationToken)
    {
        if (!PlanPaths.IsHex(reviewDigest, 64))
        { return false; }
        JsonObject binding;
        try
        { binding = await PrepareAsync(review, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is BridgeCallException or PackFormatException)
        { return false; }
        IReadOnlyList<JsonObject> records;
        try
        { records = await audit.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (AuditUnavailableException)
        { return false; }
        string previous = string.Empty;
        JsonObject? confirmed = null;
        foreach (JsonObject record in records)
        {
            if (AuditVerifier.Check(record.ToJsonString(AuditLog.Json), previous, out string hash) is not null)
            { return false; }
            previous = hash;
            if ((string?)record["kind"] == "knowledge_review" && JsonNode.DeepEquals(record["resource"], binding["resource"]))
            { confirmed = hash == reviewDigest ? record : null; }
        }
        return confirmed is not null && (string?)confirmed["kind"] == "knowledge_review" && (string?)confirmed["confirmation"] == "Granted"
            && !string.IsNullOrWhiteSpace((string?)confirmed["channel"])
            && binding.All(member => JsonNode.DeepEquals(member.Value, confirmed[member.Key]));
    }

    private async Task<JsonObject> PrepareAsync(KnowledgeReview review, CancellationToken cancellationToken)
    {
        AssetEntry resource = await ReadAsync(review.Resource, "/resource", cancellationToken).ConfigureAwait(false);
        JsonObject source = Source(review.Source, resource);
        AssetEntry origin = await ReadAsync(source["asset"]!.GetValue<string>(), "/source/asset", cancellationToken).ConfigureAwait(false);
        JsonObject binding = Bind(review, resource, origin);
        if (review.TargetContent is { } bytes)
        {
            var types = new Dictionary<string, RuleValueType>(StringComparer.Ordinal);
            string folder = packs.Locate(resource.Pack, resource.Version)!.Value.Folder;
            PackManifest manifest = PackJson.ReadManifest(await File.ReadAllBytesAsync(Path.Combine(folder, PackManifest.FileName), cancellationToken).ConfigureAwait(false));
            foreach (PackResource schema in manifest.Resources.Where(r => r.Kind == ResourceKind.ConfigSchema))
            {
                AssetEntry resolved = await ReadAsync(AssetNames.Id(resource.Pack, resource.Version, AssetNames.Local(schema)), "/spec", cancellationToken).ConfigureAwait(false);
                if (resolved.Document is not { Kind: KnowledgeKind.ConfigSchema } schemaDocument)
                { throw Failure(ErrorReason.PackInvalid, "/spec: configuration types require bundled config_schema documents."); }
                JsonElement spec = JsonSerializer.SerializeToElement(schemaDocument.Spec);
                ConfigValidator.Schema(spec);
                foreach (JsonProperty item in spec.GetProperty("items").EnumerateObject())
                {
                    string type = item.Value.GetProperty("type").GetString()!;
                    var declared = new RuleValueType(type == "choice" ? "text" : type, item.Value.TryGetProperty("unit", out JsonElement unit) ? unit.GetString() : null);
                    if (types.TryGetValue(item.Name, out RuleValueType prior) && prior != declared)
                    { throw Failure(ErrorReason.PackInvalid, "/spec: configuration operand types are ambiguous."); }
                    types[item.Name] = declared;
                }
            }
            _ = RuleValidator.Validate(JsonSerializer.SerializeToElement(KnowledgeDocument.Read(bytes).Spec), Configuration ?? (name => types.TryGetValue(name, out RuleValueType type) ? type : null));
        }
        return binding;
    }

    internal static JsonObject Bind(KnowledgeReview review, AssetEntry resource, AssetEntry? origin)
    {
        if (!PlanPaths.IsHex(review.ContentDigest, 64) || resource.Id != review.Resource || resource.Resource.Sha256 != review.ContentDigest)
        { throw Failure(ErrorReason.PlanStale, "/contentDigest: content changed; read the resource and review again."); }
        bool candidate = resource.Document?.Kind == KnowledgeKind.Candidate;
        if (!(candidate && review.Decision is "accept" or "reject" or "promote" || resource.Document?.Kind == KnowledgeKind.Gap && review.Decision == "close_gap"))
        { throw Failure(ErrorReason.PackInvalid, "/decision: accept/reject/promote needs a candidate; close_gap needs a gap. Acceptance never grants official standing."); }
        JsonObject source = Source(review.Source, resource);
        if (origin is null || origin.Id != (string?)source["asset"] || origin.Resource.Sha256 != (string?)source["hash"] || origin.Resource.Files != 1)
        { throw Failure(ErrorReason.PackInvalid, "/source/hash: the source must resolve to a file with this digest; read its current content."); }
        var binding = new JsonObject { ["resource"] = resource.Id, ["contentDigest"] = review.ContentDigest, ["decision"] = review.Decision, ["source"] = source };
        if (review.Decision == "promote")
        {
            if (review.TargetContent is null || review.TargetContent.Length > PackJson.MaxFileBytes)
            { throw Failure(ErrorReason.PackInvalid, "/targetContent: supply the new Rule's exact bytes, at most 16 MiB."); }
            KnowledgeDocument rule = KnowledgeDocument.Read(review.TargetContent);
            string target = AssetNames.Id(resource.Pack, resource.Version, "rule/" + rule.Metadata.Name);
            if (rule.Kind != KnowledgeKind.Rule || rule.ApiVersion != KnowledgeDocument.V2ApiVersion || target != review.TargetResource
                || !JsonNode.DeepEquals(Source(JsonSerializer.SerializeToElement(rule.Spec["source"]), resource), source))
            { throw Failure(ErrorReason.PackInvalid, "/targetResource: promotion needs a new v2 Rule in the same pack with the reviewed source."); }
            binding["targetResource"] = target;
            binding["targetDigest"] = PlanStore.Hash(review.TargetContent);
        }
        else if (review.TargetResource is not null || review.TargetContent is not null)
        { throw Failure(ErrorReason.PackInvalid, "/targetResource: only promotion may name a new Rule."); }
        return binding;
    }

    private static JsonObject Source(JsonElement source, AssetEntry resource)
    {
        JsonElement value = source;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 3
            || !value.TryGetProperty("asset", out JsonElement asset) || asset.ValueKind != JsonValueKind.String
            || !value.TryGetProperty("location", out JsonElement location) || !KnowledgeSpecs.IsText(JsonNode.Parse(location.GetRawText()), KnowledgeSpecs.MaxShortText) || string.IsNullOrWhiteSpace(location.GetString())
            || !value.TryGetProperty("hash", out JsonElement hash) || hash.ValueKind != JsonValueKind.String || !PlanPaths.IsHex(hash.GetString()!, 64))
        { throw Failure(ErrorReason.PackInvalid, "/source: supply only asset, location and a lowercase SHA-256 hash; confirmation text is not proof."); }
        string id = asset.GetString()!;
        if (value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 3)
        { throw Failure(ErrorReason.PackInvalid, "/source: duplicate members are forbidden."); }
        return new JsonObject { ["asset"] = AssetNames.TryParse(id, out _, out _, out _) ? id : AssetNames.Id(resource.Pack, resource.Version, id), ["location"] = location.GetString(), ["hash"] = hash.GetString() };
    }

    private async Task<AssetEntry> ReadAsync(string id, string pointer, CancellationToken cancellationToken)
    {
        if (!AssetNames.TryParse(id, out string? pack, out string? version, out string? local) || !PackLibrary.IsVersion(version)
            || packs.Locate(pack, version) is not (string folder, _))
        { throw Failure(ErrorReason.PackInvalid, pointer + ": the resource is missing; use an exact asset reference."); }
        var paths = new PlanPaths(packs.Locations.Root);
        _ = paths.Check(Path.Combine(folder, PackManifest.FileName));
        (PackManifest? manifest, _, IReadOnlyList<PackProblem> problems) = await PackVerifier.ReadManifestAsync(folder, cancellationToken).ConfigureAwait(false);
        if (manifest is null || problems.Count != 0 || manifest.Id != pack || AssetNames.VersionOf(manifest) != version)
        { throw Failure(ErrorReason.PackInvalid, pointer + ": the manifest is invalid or does not match the exact pack reference."); }
        PackResource? item = manifest.Resources.SingleOrDefault(r => AssetNames.Local(r) == local);
        if (item is null)
        { throw Failure(ErrorReason.PackInvalid, pointer + ": the resource is missing from the manifest."); }
        _ = paths.Check(Path.Combine(folder, item.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (await PackVerifier.ResourceProblemAsync(folder, item, packs.Config.PackSources, cancellationToken).ConfigureAwait(false) is not null)
        { throw Failure(ErrorReason.PackInvalid, pointer + ": source files are missing or changed; check the pack and review again."); }
        KnowledgeDocument? document = null;
        if (item.Path.StartsWith("knowledge/", StringComparison.Ordinal) && item.Storage == ResourceStorage.Bundled)
        {
            using var stream = new FileStream(Path.Combine(folder, item.Path), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > PackJson.MaxFileBytes)
            { throw Failure(ErrorReason.PackInvalid, pointer + ": knowledge exceeds 16 MiB."); }
            byte[] bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (PlanStore.Hash(bytes) != item.Sha256)
            { throw Failure(ErrorReason.PlanStale, pointer + ": knowledge content changed while reading; review again."); }
            document = KnowledgeDocument.Read(bytes);
            if (KnowledgeDocument.ResourceKindOf(document.Kind) != item.Kind || KnowledgeDocument.PathOf(document.Kind, document.Metadata.Name) != item.Path)
            { throw Failure(ErrorReason.PackInvalid, pointer + ": knowledge kind/name does not match its resource."); }
        }
        return new AssetEntry(pack, version, item, document, null, null);
    }

    private static BridgeCallException Failure(string reason, string message) => new(new BridgeError { Reason = reason, Message = message });
}
