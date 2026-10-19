namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One part of the project's own part data (<c>Project.Articles</c>) as <see cref="IPartReader"/> reads it.</summary>
public sealed class ArticleRecord
{
    /// <summary><c>Article.PartNr</c>.</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary><c>Article.VariantNr</c>; empty when there is none.</summary>
    public string Variant { get; set; } = string.Empty;

    /// <summary>The display text of <c>ARTICLE_DESCR1</c> in the requested language; null when not set.</summary>
    public string? Description { get; set; }

    /// <summary>The display text of <c>ARTICLE_MANUFACTURER</c>, the manufacturer's short name; null when not set.</summary>
    public string? Manufacturer { get; set; }
}
