namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.LayoutRemovePlan"/> and <see cref="OperationNames.LayoutRemoveExecute"/>.</summary>
public sealed class LayoutRemoveArgs
{
    /// <summary>Project name or path; required for a write.</summary>
    public string? Project { get; set; }

    /// <summary>Installation space designation, e.g. <c>UC1</c>.</summary>
    public string? Space { get; set; }

    /// <summary>Object IDs of rails, ducts and device placements from eplan_layout_get.</summary>
    public List<long>? Ids { get; set; }

    /// <summary>For the execution: the fingerprint the plan returned.</summary>
    public string? Fingerprint { get; set; }
}
