using System.Text.Json;

namespace EplanMcp.Packs;

public sealed record RuleValue(RuleValueType Type, JsonElement Value, bool Complete = true, string? Evidence = null, bool Inferred = false);
