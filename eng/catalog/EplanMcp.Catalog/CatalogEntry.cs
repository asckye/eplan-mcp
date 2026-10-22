namespace EplanMcp.Catalog;

/// <summary>A parameter of a method or indexed property.</summary>
internal sealed record Parameter(string Name, string Type);

/// <summary>Why a rule leaves an entry out of its layer's denominator: a design exclusion reason and the rule's name.</summary>
internal sealed record Exclusion(string Reason, string Rule);

/// <summary>
/// One public type or member of the EPLAN API (catalog/schema/catalog.schema.json, $defs/entry). <see cref="Id"/> is the XML
/// documentation comment ID; the other fields are facts read from metadata, never documentation text.
/// </summary>
internal sealed record CatalogEntry
{
    public required string Id { get; init; }

    /// <summary>type: class, struct, interface, enum, delegate; member: method, constructor, lifecycle, operator,
    /// delegate_method, property, field, constant, enum_value, event.</summary>
    public required string Kind { get; init; }

    public required string Assembly { get; init; }

    public required string Namespace { get; init; }

    public required string Name { get; init; }

    /// <summary>operation, data_dictionary or type; set by <see cref="Classifier"/>.</summary>
    public string Layer { get; set; } = "";

    /// <summary>The declaring type's ID for members and nested types.</summary>
    public string? DeclaringType { get; init; }

    /// <summary>Overload group of an operation: <c>O:</c> plus the declaring type and the name.</summary>
    public string? Group { get; set; }

    /// <summary>"protected" for members visible only to derived types; null means public.</summary>
    public string? Access { get; init; }

    public bool Static { get; init; }

    public string? Returns { get; init; }

    public IReadOnlyList<Parameter>? Parameters { get; init; }

    /// <summary>Properties: get, set or get;set (visible accessors only).</summary>
    public string? Accessors { get; init; }

    /// <summary>Data dictionary: the object type the property ID belongs to (Function, Page, ...).</summary>
    public string? Object { get; set; }

    /// <summary>Constants and enum values, invariant culture.</summary>
    public string? Value { get; init; }

    /// <summary>Types: base type, implemented interfaces, abstract/sealed/static.</summary>
    public string? Base { get; init; }

    public IReadOnlyList<string>? Interfaces { get; init; }

    public string? Modifiers { get; init; }

    /// <summary>The ID appears in the assembly's IntelliSense file.</summary>
    public bool Documented { get; set; }

    public bool Obsolete { get; init; }

    public Exclusion? Excluded { get; set; }

    /// <summary>The signature uses native C++ types (pointers, global-namespace types): not callable from C#.</summary>
    public bool NativeSignature { get; init; }
}
