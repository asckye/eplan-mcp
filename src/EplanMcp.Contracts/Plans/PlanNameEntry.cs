namespace EplanMcp.Contracts;

/// <summary>One occupied name, preserved without normalization or case folding.</summary>
public sealed class PlanNameEntry
{
    /// <summary>Either page or function.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The original full name; function names are non-empty.</summary>
    public string Name { get; set; } = string.Empty;
}
