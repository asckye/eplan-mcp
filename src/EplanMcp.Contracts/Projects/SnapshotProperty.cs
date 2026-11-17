namespace EplanMcp.Contracts;

/// <summary>
/// One property of a snapshot profile's registered set (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.2): the only
/// properties a snapshot reads, and the only ones rules may refer to (M6 design 4.11).
/// </summary>
public sealed class SnapshotProperty
{
    /// <summary>Kind of a plain text value, kept as EPLAN stores it.</summary>
    public const string Text = "text";

    /// <summary>Kind of a multi-language text, split into one entry per language.</summary>
    public const string MultiLanguage = "multilanguage";

    /// <summary>Kind of a modification date or time: returned as a change hint, never part of a content hash.</summary>
    public const string Hint = "hint";

    public SnapshotProperty(string scope, int id, string name, string kind)
    {
        Scope = scope;
        Id = id;
        Name = name;
        Kind = kind;
    }

    /// <summary>Which objects carry it: <c>page</c>, <c>function</c> (every function), <c>terminal</c>, <c>plc</c>, <c>cable</c> or <c>connection</c>.</summary>
    public string Scope { get; }

    /// <summary>EPLAN's property number, from the capability catalog's data dictionary.</summary>
    public int Id { get; }

    /// <summary>EPLAN's property name, for example <c>FUNC_TEXT</c>.</summary>
    public string Name { get; }

    /// <summary><see cref="Text"/>, <see cref="MultiLanguage"/> or <see cref="Hint"/>.</summary>
    public string Kind { get; }
}
