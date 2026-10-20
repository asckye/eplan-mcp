namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The page a macro goes on, as <see cref="IMacroInserter.ReadPage"/> reads it.</summary>
public sealed class MacroTargetPage
{
    /// <summary>The page's full name as EPLAN has it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The <c>DocumentType</c> enum name, for example <c>Circuit</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The page's width in millimetres (<c>Page.Size</c>); null when EPLAN cannot tell it, as on a new, empty page (A14).</summary>
    public double? Width { get; set; }

    /// <summary>The page's height in millimetres; null together with <see cref="Width"/>.</summary>
    public double? Height { get; set; }

    /// <summary>The session identifier (<c>ToStringIdentifier</c>, valid in this EPLAN session only, A3) of every object on the page.</summary>
    public List<string> Objects { get; set; } = [];
}
