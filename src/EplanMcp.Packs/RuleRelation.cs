namespace EplanMcp.Packs;

public sealed record RuleRelation(IReadOnlyList<string> Locators, bool Complete, string? Evidence = null, bool Inferred = false);
