namespace EplanMcp.Contracts;

/// <summary>One end of a connection in a snapshot: the function its pin belongs to, and the pin.</summary>
public sealed class SnapshotEnd
{
    /// <summary>The function's type number (the second part of its string identifier, api-notes A32).</summary>
    public int Type { get; set; }

    /// <summary>The function's <c>ObjectIdentifier</c>.</summary>
    public long Oid { get; set; }

    /// <summary>The function's full name, for reading only: the hash takes the type and the number.</summary>
    public string? Function { get; set; }

    /// <summary>The pin's name, for example <c>13</c>.</summary>
    public string? Pin { get; set; }
}
