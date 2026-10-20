namespace EplanMcp.Contracts;

/// <summary>One page of <see cref="PageUpdateArgs"/>.</summary>
public sealed class PageUpdateItem
{
    /// <summary>The full page name; matched exactly, or ignoring case when that is unique.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The new description in <see cref="PageUpdateArgs.Language"/>; empty clears that language's text.</summary>
    public string Description { get; set; } = string.Empty;
}
