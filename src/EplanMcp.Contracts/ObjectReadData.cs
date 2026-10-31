namespace EplanMcp.Contracts;
/// <summary>A bounded detached read block, produced entirely on the main thread.</summary>
public sealed class ObjectReadData
{
    /// <summary>Null when supported; otherwise an explicit capability reason.</summary>
    public string? Unsupported { get; set; }
    /// <summary>At most 200 discovered identities.</summary>
    public List<ObjectSummary> Objects { get; set; } = [];
    /// <summary>At most 200 property rows for get.</summary>
    public PropertyReadData? Properties { get; set; }
    /// <summary>The next object or selector position, or null at completion.</summary>
    public int? NextOffset { get; set; }
}
