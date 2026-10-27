namespace EplanMcp.Packs;

/// <summary>Identity and kind from the same spec whose scope/check were checked by RuleValidator.</summary>
public sealed record RuleDefinition(string Rule, string Version, string Kind, RuleNode Scope, RuleNode Check);
