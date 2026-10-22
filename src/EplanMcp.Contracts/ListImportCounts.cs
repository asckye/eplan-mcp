namespace EplanMcp.Contracts;

/// <summary>Project business counts, never a count of successfully imported rows (S34, api-notes A39).</summary>
public sealed class ListImportCounts
{
    /// <summary>Device list entries enumerated by DMObjectsFinder.GetAll&lt;DeviceListEntry&gt;(true).</summary>
    public int DeviceListEntries { get; set; }

    /// <summary>Parts in Project.Articles, independent of the configured system parts database.</summary>
    public int ProjectParts { get; set; }

    /// <summary>References returned by DMObjectsFinder.GetArticleReferences(null), as counted in S34.</summary>
    public int ArticleReferences { get; set; }
}
