namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// One page, function or connection as the snapshot adapter reads it, before normalisation (docs/design/2026-10-21-m6-bridge-snapshot.md,
/// 4.1, 4.3): raw values, which <see cref="SnapshotCanonical"/> turns into the contract. Lives for one call only.
/// </summary>
public sealed class SnapshotSourceObject
{
    /// <summary><c>page</c>, <c>function</c> or <c>connection</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// <c>StorableObject.ToStringIdentifier()</c>, <c>db/type/oid/index</c> (api-notes A32): only its type is kept, the
    /// database number changes on every opening and never leaves the Bridge.
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary><c>StorableObject.ObjectIdentifier</c>.</summary>
    public long Oid { get; set; }

    /// <summary>The page's or function's full name.</summary>
    public string? Name { get; set; }

    /// <summary>The page a function or connection is on.</summary>
    public string? Page { get; set; }

    /// <summary>A page's <c>Page.PageType</c> name.</summary>
    public string? PageType { get; set; }

    /// <summary>A function's category name.</summary>
    public string? Category { get; set; }

    /// <summary>A function's shown tag.</summary>
    public string? Shown { get; set; }

    /// <summary>Whether a function is a main function.</summary>
    public bool? Main { get; set; }

    /// <summary>A function's x position in millimetres (api-notes A3).</summary>
    public double? X { get; set; }

    /// <summary>A function's y position in millimetres.</summary>
    public double? Y { get; set; }

    /// <summary>A main function's part numbers.</summary>
    public List<string>? Parts { get; set; }

    /// <summary>A connection's start: the identifier, number and name of its pin's function, and the pin.</summary>
    public SnapshotSourceEnd? Start { get; set; }

    /// <summary>A connection's end.</summary>
    public SnapshotSourceEnd? End { get; set; }

    /// <summary>The registered properties that are not empty, hints included, each as <c>PropertyValue.ToString()</c> gave it.</summary>
    public List<SnapshotSourceValue> Values { get; set; } = [];
}
