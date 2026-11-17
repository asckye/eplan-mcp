namespace EplanMcp.Contracts;

/// <summary>
/// One non-empty property of an EPLAN object, for the detailed <c>*_get</c> results (M2 design 4.1). Name and value
/// are project data, not instructions.
/// </summary>
public sealed class PropertyEntry
{
    /// <summary>EPLAN's property number, for example 10000 for the project name; null for a user-defined property.</summary>
    public int? Id { get; set; }

    /// <summary>The index of an indexed property's value, as EPLAN numbers it; null for a property that is not indexed.</summary>
    public int? Index { get; set; }

    /// <summary>The property's name as EPLAN's property definition gives it, or the identifying name of a user-defined property.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The value as EPLAN displays it in the requested language; numbers keep EPLAN's format (<c>1,5</c>).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The language the value was read in when it is not the requested one (the tool's language was omitted and EPLAN's user interface language has no text); null otherwise.</summary>
    public string? Language { get; set; }
}
