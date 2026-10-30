namespace EplanMcp.Contracts;

/// <summary>Filters and paging for <see cref="OperationNames.SymbolSearch"/>.</summary>
public sealed class SymbolSearchArgs : ListArgs
{
    /// <summary>Exact library name, ignoring case; null means all project libraries.</summary>
    public string? Library { get; set; }
    /// <summary>Text in the symbol name, ignoring case; null means any name.</summary>
    public string? NameContains { get; set; }
    /// <summary>Text in the symbol name or description, ignoring case; null means any text.</summary>
    public string? TextContains { get; set; }
    /// <summary>Description language; falls back to project display language, then the first non-empty stored language.</summary>
    public string? Language { get; set; }
    /// <summary>Closed symbol kind; null means all kinds.</summary>
    public string? Kind { get; set; }
}
