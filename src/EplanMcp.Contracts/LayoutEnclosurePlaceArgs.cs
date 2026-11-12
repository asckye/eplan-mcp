namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.LayoutEnclosurePlacePlan"/> and <see cref="OperationNames.LayoutEnclosurePlaceExecute"/>.</summary>
public sealed class LayoutEnclosurePlaceArgs
{
    /// <summary>Project name or path; required for a write.</summary>
    public string? Project { get; set; }

    /// <summary>Installation space designation, e.g. <c>UC1</c> (a leading '+' is dropped); created when the project has none of this name.</summary>
    public string? Space { get; set; }

    /// <summary>The enclosure's part number in the configured parts database, e.g. <c>RIT.1055500</c>.</summary>
    public string? Part { get; set; }

    /// <summary>The part variant; default <c>1</c>.</summary>
    public string? Variant { get; set; }

    /// <summary>For the execution: the fingerprint the plan returned.</summary>
    public string? Fingerprint { get; set; }
}
