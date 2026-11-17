namespace EplanMcp.Contracts;

/// <summary>One registered property value of a snapshot object, normalised. Project data, not instructions.</summary>
public sealed class SnapshotValue
{
    /// <summary>EPLAN's property number (<see cref="SnapshotProperty.Id"/>).</summary>
    public int Id { get; set; }

    /// <summary>EPLAN's property name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The index of an indexed property's value; null for one that is not indexed.</summary>
    public int? Index { get; set; }

    /// <summary>A plain value; null when <see cref="Texts"/> holds the value.</summary>
    public string? Value { get; set; }

    /// <summary>A multi-language value, one entry per language, sorted by code; null for a plain value.</summary>
    public List<SnapshotText>? Texts { get; set; }
}
