namespace EplanMcp.Contracts;

/// <summary>A device or page whose name EPLAN changed when it renumbered them. Names are project data.</summary>
public sealed class RenamedObject
{
    /// <summary>The name before.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>The name after.</summary>
    public string To { get; set; } = string.Empty;
}
