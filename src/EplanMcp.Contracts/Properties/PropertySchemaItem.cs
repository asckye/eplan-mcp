namespace EplanMcp.Contracts;

/// <summary>A dictionary definition and independently reported read/write permissions (design 4.2–4.4).</summary>
public sealed class PropertySchemaItem
{
    /// <summary>The dictionary ID within the enclosing dictionary object type.</summary>
    public int PropertyId { get; set; }

    /// <summary>The exact formal symbolic name.</summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>Read permission after shipped classification; null means unknown.</summary>
    public bool? ReadAllowed { get; set; }

    /// <summary>The read restriction or unknown-state reason.</summary>
    public string? ReadReason { get; set; }

    /// <summary>Runtime API writability only; null without a verified runtime definition.</summary>
    public bool? ApiWritable { get; set; }

    /// <summary>Published whitelist permission only; null means unknown and never grants a write.</summary>
    public bool? WriteAllowed { get; set; }

    /// <summary>The write restriction or unknown-state reason.</summary>
    public string? WriteReason { get; set; }

    /// <summary>Known type, bounds, index, unit and language facts; absent fields remain unknown.</summary>
    public PropertyDefinitionMetadata Definition { get; set; } = new();

    /// <summary>The dictionary or verification source.</summary>
    public string? Source { get; set; }

    /// <summary>The version for which these facts were recorded.</summary>
    public string? Version { get; set; }

    /// <summary>Differences between the runtime definition and published facts, when an instance was inspected.</summary>
    public List<string>? RuntimeDifferences { get; set; }
}
