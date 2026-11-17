namespace EplanMcp.Contracts;

/// <summary>Closed tagged union: only the fields belonging to the selected tag may be non-null.</summary>
public sealed class PlanGuardCondition
{
    /// <summary>objectHash, pageHash, members, absent, resource or binding.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>objectHash/pageHash: the snapshot locator.</summary>
    public ObjectLocator? Locator { get; set; }

    /// <summary>members: exact scope.</summary>
    public PlanGuardScope? Scope { get; set; }

    /// <summary>objectHash/pageHash/members: lowercase SHA-256.</summary>
    public string? Hash { get; set; }

    /// <summary>members: expected object count, including pages.</summary>
    public int? Count { get; set; }

    /// <summary>absent: page or function.</summary>
    public string? Kind { get; set; }

    /// <summary>absent: full name, compared ignoring case, including device terminal descendants.</summary>
    public string? Name { get; set; }

    /// <summary>resource: expected file SHA-256.</summary>
    public string? Sha256 { get; set; }

    /// <summary>binding: expected workspace revision.</summary>
    public long? Revision { get; set; }

    /// <summary>binding: expected canonical lock digest.</summary>
    public string? LockDigest { get; set; }
}
