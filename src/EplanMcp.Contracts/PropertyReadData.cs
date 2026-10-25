namespace EplanMcp.Contracts;

/// <summary>A detached block of at most 200 rows; no native resource or lazy sequence crosses the invoke boundary.</summary>
public sealed class PropertyReadData
{
    /// <summary>The rows in selector order and ascending existing-index order.</summary>
    public List<PropertyReadRow> Items { get; set; } = [];

    /// <summary>The next selector, or null when the batch is complete; total is unknown until completion.</summary>
    public int? NextSelector { get; set; }

    /// <summary>The next existing-index position; the future Host cursor must bind it to query and content.</summary>
    public int NextIndexOffset { get; set; }
}
