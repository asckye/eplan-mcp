namespace EplanMcp.Contracts;

/// <summary>One function whose structure can follow the target page (A14); names are data, not regular expressions.</summary>
public sealed class PlanMacroFunction
{
    /// <summary>function, terminal, plc or cable.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The FunctionCategory enum name.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The full macro name, including literal structure and numbering placeholders.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The displayed macro name, or empty when none is shown.</summary>
    public string VisibleName { get; set; } = string.Empty;

    /// <summary>Whether this is a main function.</summary>
    public bool Main { get; set; }

    /// <summary>Ordered structure segments, including their =/+ prefixes; an empty segment inherits the target page.</summary>
    public List<string> StructureSegments { get; set; } = [];
}
