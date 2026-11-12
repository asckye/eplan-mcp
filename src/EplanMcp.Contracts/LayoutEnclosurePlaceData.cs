namespace EplanMcp.Contracts;

/// <summary>Data of <see cref="OperationNames.LayoutEnclosurePlaceExecute"/>: the enclosure as read back after the commit.</summary>
public sealed class LayoutEnclosurePlaceData
{
    /// <summary>The resolved project name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The installation space designation.</summary>
    public string Space { get; set; } = string.Empty;

    /// <summary>True when the space was created by this call.</summary>
    public bool SpaceCreated { get; set; }

    /// <summary>The placed enclosure with its mounting panel and door surfaces.</summary>
    public LayoutEnclosure Enclosure { get; set; } = new();

    /// <summary>A problem after the commit that did not undo it, e.g. a failed release; null when none.</summary>
    public string? Warning { get; set; }
}
