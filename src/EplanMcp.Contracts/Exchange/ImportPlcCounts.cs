namespace EplanMcp.Contracts;

/// <summary>Business counts read with DMObjectsFinder as in S34 (api-notes A39).</summary>
public sealed class ImportPlcCounts
{
    /// <summary>All functions, including those without a page.</summary>
    public int Functions { get; set; }

    /// <summary>PLC boxes from GetPLCs(null).</summary>
    public int PlcBoxes { get; set; }

    /// <summary>Article references from GetArticleReferences(null).</summary>
    public int ArticleReferences { get; set; }
}
