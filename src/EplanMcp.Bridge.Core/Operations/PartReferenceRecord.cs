namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One part reference as <see cref="IPartReader"/> reads it: EPLAN's values, unchanged.</summary>
public sealed class PartReferenceRecord
{
    /// <summary><c>ArticleReference.PartNr</c>.</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary><c>ArticleReference.VariantNr</c>; empty when there is none.</summary>
    public string Variant { get; set; } = string.Empty;

    /// <summary><c>ArticleReference.Count</c>.</summary>
    public int Count { get; set; }

    /// <summary>What the reference belongs to.</summary>
    public PartOwner Owner { get; set; }

    /// <summary><c>Function.Name</c>, the full tag, when <see cref="Owner"/> is a function; empty otherwise or without a name.</summary>
    public string OwnerName { get; set; } = string.Empty;
}
