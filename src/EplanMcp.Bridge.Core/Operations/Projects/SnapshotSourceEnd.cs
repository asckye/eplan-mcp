namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One end of a connection as the snapshot adapter reads it: the pin's function and the pin's name.</summary>
public sealed class SnapshotSourceEnd
{
    /// <summary>The function's <c>ToStringIdentifier()</c>, for its type number.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>The function's <c>ObjectIdentifier</c>.</summary>
    public long Oid { get; set; }

    /// <summary>The function's full name.</summary>
    public string? Function { get; set; }

    /// <summary>The pin's name.</summary>
    public string? Pin { get; set; }
}
