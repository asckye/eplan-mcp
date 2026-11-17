namespace EplanMcp.Contracts;

/// <summary>Detached raw B1 evidence for re-planning and post-write side-effect checks; never a display DTO.</summary>
public sealed class PropertySetGuard
{
    /// <summary>Complete identity checks, including category and position.</summary>
    public ObjectLocator Target { get; set; } = new();

    /// <summary>The exact runtime dictionary type; no inherited permission.</summary>
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>Whether this related function is a main function.</summary>
    public bool Main { get; set; }

    /// <summary>Part assignments to preserve; null means unavailable.</summary>
    public List<string>? Parts { get; set; }

    /// <summary>The guarded property ID.</summary>
    public int PropertyId { get; set; }

    /// <summary>Copied runtime definition, fingerprinted to detect drift.</summary>
    public PropertyDefinitionMetadata Definition { get; set; } = new();

    /// <summary>The native empty state, distinguished from a present value containing empty translations.</summary>
    public bool WasEmpty { get; set; }

    /// <summary>All original stored languages, including independent text, without cleaning or truncation.</summary>
    public List<SnapshotText> Before { get; set; } = [];

    /// <summary>The complete expected translations; auxiliary functions retain their original values.</summary>
    public List<SnapshotText> After { get; set; } = [];
}
