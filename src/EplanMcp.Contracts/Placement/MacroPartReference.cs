namespace EplanMcp.Contracts;

/// <summary>A part reference read from an inserted main function; strings are project data.</summary>
public sealed class MacroPartReference
{
    /// <summary>The main function's full tag.</summary>
    public string Tag { get; set; } = string.Empty;
    /// <summary>The native ArticleReference.PartNr.</summary>
    public string PartNumber { get; set; } = string.Empty;
    /// <summary>The native ArticleReference.VariantNr.</summary>
    public string Variant { get; set; } = string.Empty;
    /// <summary>The native ArticleReference.Count.</summary>
    public int Count { get; set; }
}
