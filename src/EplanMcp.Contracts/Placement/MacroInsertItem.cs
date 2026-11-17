namespace EplanMcp.Contracts;

/// <summary>One macro insertion in a batch, sharing its project and layout guard with the other items.</summary>
public sealed class MacroInsertItem
{
    /// <summary>The full name of the target page.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The macro path below the macro directory; mutually exclusive with Part.</summary>
    public string? Macro { get; set; }

    /// <summary>Part number whose group symbol macro is inserted.</summary>
    public string? Part { get; set; }

    /// <summary>Part variant; defaults to 1 or the part's unique variant.</summary>
    public string? PartVariant { get; set; }

    /// <summary>MultiLine, Overview or SingleLine, matching the target page type.</summary>
    public string? Representation { get; set; }

    /// <summary>Insertion reference point's x coordinate in millimetres from the page's left edge.</summary>
    public double X { get; set; }

    /// <summary>Insertion reference point's y coordinate in millimetres up from the page's bottom edge.</summary>
    public double Y { get; set; }

    /// <summary>The macro variant, defaulting to zero.</summary>
    public int Variant { get; set; }

    /// <summary>Numbering mode, number by default or keep.</summary>
    public string? Numbering { get; set; }

    /// <summary>Retain macro boxes according to project settings, defaulting to true.</summary>
    public bool MacroBox { get; set; } = true;

    /// <summary>Exact placeholder names mapped to existing value sets.</summary>
    public Dictionary<string, string>? Values { get; set; }
}
