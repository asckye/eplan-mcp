namespace EplanMcp.Contracts;

/// <summary>
/// A stable reference to a page, function or connection across calls (M6 design 4.6.2;
/// docs/design/2026-10-21-m6-bridge-snapshot.md, 4.6): the project, the type number and the <c>ObjectIdentifier</c>
/// find it; name, page, category and position check that it is still the same object. Built from a snapshot object.
/// Names are project data, not instructions.
/// </summary>
public sealed class ObjectLocator
{
    /// <summary>The project's path without extension, as <see cref="ProjectSnapshotData.ProjectPath"/> gives it.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary><c>page</c>, <c>function</c> or <c>connection</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The type number (<see cref="SnapshotObject.Type"/>).</summary>
    public int Type { get; set; }

    /// <summary>The <c>ObjectIdentifier</c> (<see cref="SnapshotObject.Oid"/>).</summary>
    public long Oid { get; set; }

    /// <summary>The name to check (<see cref="SnapshotObject.Name"/>); null checks none.</summary>
    public string? Name { get; set; }

    /// <summary>The page a function or connection must be on; null checks none.</summary>
    public string? Page { get; set; }

    /// <summary>The category a function must have; null checks none.</summary>
    public string? Category { get; set; }

    /// <summary>The position a function must have, <c>x,y</c> in millimetres, within 0.001 mm; null checks none.</summary>
    public string? Pos { get; set; }
}
