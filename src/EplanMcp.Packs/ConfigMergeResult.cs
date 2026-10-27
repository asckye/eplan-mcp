using System.Text.Json;

namespace EplanMcp.Packs;

/// <summary>Effective items, or the first problem with its source trace; failures expose no partial values.</summary>
public sealed record ConfigMergeResult(JsonElement Values, string? Reason, string? Location, string? Message, JsonElement Trace)
{
    public bool Ok => Reason is null;
}
