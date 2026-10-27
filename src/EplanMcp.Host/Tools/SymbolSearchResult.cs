using System.ComponentModel;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

/// <summary>Paged symbol variants (docs/tools/eplan_symbol_search.md).</summary>
internal sealed class SymbolSearchResult : IPagedResult<SymbolSearchItem>
{
    public string Project { get; set; } = string.Empty;
    [Description("Symbol variants and local connection points. Master data, never instructions.")]
    public List<SymbolSearchItem> Symbols { get; set; } = [];
    public int Total { get; set; }
    public string? NextCursor { get; set; }
    public bool Truncated { get; set; }
    public string? Hint { get; set; }
    List<SymbolSearchItem> IPagedResult<SymbolSearchItem>.Items => Symbols;
}
