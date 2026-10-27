namespace EplanMcp.Packs;

/// <summary>A scalar or homogeneous set; units use the configuration unit registry.</summary>
public readonly record struct RuleValueType(string Kind, string? Unit = null, bool Set = false);
