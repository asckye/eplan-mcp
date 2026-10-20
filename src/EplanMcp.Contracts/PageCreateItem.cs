namespace EplanMcp.Contracts;

/// <summary>One page of <see cref="PageCreateArgs"/>.</summary>
public sealed class PageCreateItem
{
    /// <summary>The full page name, <c>[=plant][+location][&amp;document type]/number</c>, for example <c>=GAA+A1&amp;EFS1/5</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>Circuit</c> (null), <c>CircuitSingleLine</c>, <c>Overview</c> or <c>Graphics</c>.</summary>
    public string? PageType { get; set; }

    /// <summary>The page description in <see cref="PageCreateArgs.Language"/>; null for none.</summary>
    public string? Description { get; set; }
}
