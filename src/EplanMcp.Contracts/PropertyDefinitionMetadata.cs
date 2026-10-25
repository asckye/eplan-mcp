namespace EplanMcp.Contracts;

/// <summary>Verified definition facts shared by schema and whitelist entries; null means unknown, never unrestricted.</summary>
public sealed class PropertyDefinitionMetadata
{
    /// <summary>The value type from design 4.3; opaque is display-only and cannot be written.</summary>
    public string? ValueType { get; set; }

    /// <summary>Runtime read-only status; setter availability alone does not establish writability.</summary>
    public bool? IsReadOnly { get; set; }

    /// <summary>Whether the value is computed and therefore unsuitable for bare assignment.</summary>
    public bool? IsComputed { get; set; }

    /// <summary>Whether the property participates in an object's name.</summary>
    public bool? IsNamePart { get; set; }

    /// <summary>Whether the runtime definition marks this as internal.</summary>
    public bool? IsInternal { get; set; }

    /// <summary>Whether the runtime definition is licensed in the inspected environment.</summary>
    public bool? IsPropertyLicensed { get; set; }

    /// <summary>The verified maximum text length, before applying the stricter product budget.</summary>
    public int? MaximumLength { get; set; }

    /// <summary>The inclusive typed lower bound, preserving integer precision.</summary>
    public PropertyTypedValue? LowerBound { get; set; }

    /// <summary>The inclusive typed upper bound.</summary>
    public PropertyTypedValue? UpperBound { get; set; }

    /// <summary>Published enum names or numeric members, exclusively one representation per entry.</summary>
    public List<PropertyTypedValue>? EnumValues { get; set; }

    /// <summary>The verified storage/input format, not a culture-dependent guess.</summary>
    public string? Format { get; set; }

    /// <summary>Whether empty text may clear one scalar or one language.</summary>
    public bool? EmptyAllowed { get; set; }

    /// <summary>Whether an explicit index is required for writes.</summary>
    public bool? IsIndexed { get; set; }

    /// <summary>The inclusive legal minimum index; never assume zero or one.</summary>
    public int? MinimumIndex { get; set; }

    /// <summary>The inclusive legal maximum index; instance restrictions still apply.</summary>
    public int? MaximumIndex { get; set; }

    /// <summary>The verified storage unit; no implicit conversion is permitted.</summary>
    public string? Unit { get; set; }

    /// <summary>The verified language mode, including explicit-language merge/preserve semantics.</summary>
    public string? LanguageMode { get; set; }
}
