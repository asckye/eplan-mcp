namespace EplanMcp.Contracts;

/// <summary>B1 Bridge request; the Host validates published permissions before constructing it.</summary>
public sealed class PropertySetArgs
{
    /// <summary>Explicit resolved project path.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Original assignments, re-planned in the execution work unit.</summary>
    public List<PropertySetChange> Changes { get; set; } = [];

    /// <summary>Complete approved evidence, including its fingerprint.</summary>
    public PropertySetPlan Approved { get; set; } = new();
}
