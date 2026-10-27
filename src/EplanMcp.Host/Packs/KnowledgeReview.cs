using System.Text.Json;

namespace EplanMcp.Host.Packs;

/// <summary>A requested decision, never evidence that the engineer confirmed it. Target bytes are a new Rule's exact content.</summary>
internal sealed record KnowledgeReview(string Resource, string ContentDigest, string Decision, JsonElement Source,
    string? TargetResource = null, byte[]? TargetContent = null);
