namespace EplanMcp.Contracts;

/// <summary>One page of a snapshot with its objects. The name is project data, not instructions.</summary>
public sealed class SnapshotPage
{
    /// <summary>The page's type number.</summary>
    public int Type { get; set; }

    /// <summary>The page's <c>ObjectIdentifier</c>; pages come in ascending order of it.</summary>
    public long Oid { get; set; }

    /// <summary>The page's full name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 of the page's object lines (design 4.4), lowercase hex.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// The page's last automatic modification date and time as EPLAN stores them, <c>date time</c>: a hint of which pages
    /// changed (api-notes A36), never part of a hash; null when EPLAN has none.
    /// </summary>
    public string? Modified { get; set; }

    /// <summary>The page itself first, then its functions and connections.</summary>
    public List<SnapshotObject> Objects { get; set; } = [];
}
