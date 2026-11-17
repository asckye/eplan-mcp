namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.LayoutRemoveExecute"/>.</summary>
public sealed class LayoutRemoveData
{
    /// <summary>The project.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The installation space.</summary>
    public string Space { get; set; } = string.Empty;

    /// <summary>The removed objects, as they were before.</summary>
    public List<LayoutRemoved> Removed { get; set; } = [];

    /// <summary>A note from the transaction; null when none.</summary>
    public string? Warning { get; set; }
}

/// <summary>One removed layout object.</summary>
public sealed class LayoutRemoved
{
    /// <summary>Its object ID before the removal.</summary>
    public long Id { get; set; }

    /// <summary>MountingRail, Duct or Component.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The device tag; null for rails and ducts.</summary>
    public string? Tag { get; set; }

    /// <summary>Its part numbers.</summary>
    public List<string> Parts { get; set; } = [];
}
