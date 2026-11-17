namespace EplanMcp.Contracts;

/// <summary>
/// One object of a snapshot page: the page itself, a function or a connection (docs/design/2026-10-21-m6-bridge-snapshot.md,
/// 4.1). Kind, type and number locate it within the project; names and values are project data, not instructions.
/// </summary>
public sealed class SnapshotObject
{
    /// <summary><c>page</c>, <c>function</c> or <c>connection</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The type number (the second part of the object's string identifier, api-notes A32).</summary>
    public int Type { get; set; }

    /// <summary>The <c>ObjectIdentifier</c>: unchanged across file copies, backup and restore, PXF and reopening (A32, A34).</summary>
    public long Oid { get; set; }

    /// <summary>
    /// The page's full name, the function's full name, or for a connection <c>type/oid:pin&gt;type/oid:pin</c> of its
    /// two ends, as object.resolve checks it.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>SHA-256 of the object's canonical text, lowercase hex (design 4.4).</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>The page a function or connection is on.</summary>
    public string? Page { get; set; }

    /// <summary>A page's type, for example <c>Circuit</c>.</summary>
    public string? PageType { get; set; }

    /// <summary>A function's category, for example <c>Coil</c>.</summary>
    public string? Category { get; set; }

    /// <summary>A function's shown tag, when it has one.</summary>
    public string? Shown { get; set; }

    /// <summary>Whether a function is a main function.</summary>
    public bool? Main { get; set; }

    /// <summary>A function's position on its page, <c>x,y</c> in millimetres rounded to 0.001 (design 4.3).</summary>
    public string? Pos { get; set; }

    /// <summary>The part numbers of a main function, in EPLAN's order.</summary>
    public List<string>? Parts { get; set; }

    /// <summary>A connection's start.</summary>
    public SnapshotEnd? Start { get; set; }

    /// <summary>A connection's end.</summary>
    public SnapshotEnd? End { get; set; }

    /// <summary>The registered properties that have a value, sorted by number and index; hints are not among them.</summary>
    public List<SnapshotValue> Values { get; set; } = [];
}
