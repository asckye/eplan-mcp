namespace EplanMcp.Contracts;
/// <summary>Detached UI readback; no execution state because project data is unchanged.</summary>
public sealed class SelectionData
{
    /// <summary>Current project path, or null.</summary>
    public string? Project { get; set; }
    /// <summary>Currently edited page, or null.</summary>
    public string? Page { get; set; }
    /// <summary>Supported selection, sorted by project/page/type/number.</summary>
    public List<ObjectSummary> Objects { get; set; } = [];
    /// <summary>Supported selection count before paging.</summary>
    public int Total { get; set; }
    /// <summary>Selection entries without unambiguous discovery identities.</summary>
    public int Unsupported { get; set; }
    /// <summary>Requested identities absent from independent readback.</summary>
    public List<DrawingLocator> Unselected { get; set; } = [];
    /// <summary>Actual selection exactly equals the requested set.</summary>
    public bool Exact { get; set; }
    /// <summary>Digest of the complete detached readback before paging.</summary>
    public string Fingerprint { get; set; } = string.Empty;
}
