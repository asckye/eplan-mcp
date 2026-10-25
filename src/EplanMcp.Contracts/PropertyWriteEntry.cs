namespace EplanMcp.Contracts;

/// <summary>All evidence and constraints required by design 4.5; constructing an entry grants no permission.</summary>
public sealed class PropertyWriteEntry
{
    /// <summary>The verified EPLAN major version, part of the exact whitelist key.</summary>
    public int EplanMajorVersion { get; set; }

    /// <summary>The dictionary object type, never a runtime numeric type or an inherited grant.</summary>
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>The dictionary ID on this exact object type.</summary>
    public int PropertyId { get; set; }

    /// <summary>The verified category, empty for categoryless types; no wildcard or derived-category inheritance.</summary>
    public string ApplicableCategory { get; set; } = string.Empty;

    /// <summary>The exact formal symbolic name.</summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>Verified value type, length, bounds, enum, format, emptiness, index, unit and language rules.</summary>
    public PropertyDefinitionMetadata Definition { get; set; } = new();

    /// <summary>The concrete verified version/variant/licence combinations.</summary>
    public List<PropertyWriteEnvironment> Environments { get; set; } = [];

    /// <summary>A closed, compiled setter adapter identifier, never executable API text.</summary>
    public string SetterMode { get; set; } = string.Empty;

    /// <summary>A closed, compiled independent read-back adapter identifier.</summary>
    public string ReadBackMode { get; set; } = string.Empty;

    /// <summary>Dependent properties or atomic assignment companions to preview and fingerprint.</summary>
    public List<PropertySelector> Dependencies { get; set; } = [];

    /// <summary>Compiled side-effect guard identifiers defining the approved propagation boundary.</summary>
    public List<string> SideEffectGuards { get; set; } = [];

    /// <summary>The verified normalized comparison rule, never a display/truncated-text comparison.</summary>
    public string NormalizedComparison { get; set; } = string.Empty;

    /// <summary>Tests proving write, independent read-back, persistence, one undo, rollback and side-effect gates.</summary>
    public List<string> TestEvidence { get; set; } = [];

    /// <summary>Exact api-notes references recording verified semantics and restrictions.</summary>
    public List<string> ApiNotesEvidence { get; set; } = [];
}
