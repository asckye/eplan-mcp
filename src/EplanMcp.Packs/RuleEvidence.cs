using System.Text.Json;

namespace EplanMcp.Packs;

public sealed record RuleEvidence(string Location, string Locator, JsonElement? Value, bool Complete, string? Note);
