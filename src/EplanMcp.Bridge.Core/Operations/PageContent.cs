using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What <see cref="IPageReader.ReadPage"/> reads from one page; plain values, no EPLAN objects.</summary>
public sealed class PageContent
{
    public PageDetail Page { get; set; } = new();

    /// <summary>Every function placed on the page, main and auxiliary, in EPLAN's order.</summary>
    public IReadOnlyList<FunctionRecord> Functions { get; set; } = [];

    /// <summary>Null unless the properties were asked for.</summary>
    public List<PropertyEntry>? Properties { get; set; }
}
