namespace EplanMcp.Packs;

public sealed record RuleResult(string Rule, string Version, string Locator, RuleStatus Status, IReadOnlyList<RuleEvidence> Evidence, string Basis);
