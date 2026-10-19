namespace EplanMcp.Host.Tools;

/// <summary>
/// The paging fields every list tool's result has, so <see cref="ResultShaper.FitPage"/> can cut any of them.
/// Implement <see cref="Items"/> explicitly: it only points at the tool's own list (<c>projects</c>, <c>pages</c>, …),
/// and explicit members stay out of the JSON and the output schema.
/// </summary>
internal interface IPagedResult<TItem>
{
    public List<TItem> Items { get; }

    public string? NextCursor { get; set; }

    public bool Truncated { get; set; }

    public string? Hint { get; set; }
}
