namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.PlaceholderApplyPlan"/> and <see cref="OperationNames.PlaceholderApplyExecute"/>
/// (docs/tools/eplan_placeholder_apply.md, section 2).
/// </summary>
public sealed class PlaceholderApplyArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full name of the page the placeholder is on, found as eplan_page_get finds one.</summary>
    public string? Page { get; set; }

    /// <summary>The placeholder's name, exactly; empty for one without a name, null for any name.</summary>
    public string? Placeholder { get; set; }

    /// <summary>The name of the value set to apply, exactly (case-sensitive).</summary>
    public string? ValueSet { get; set; }

    /// <summary>The plan's fingerprint; required by the execute operation (PLAN_STALE when the placeholder changed).</summary>
    public string? Fingerprint { get; set; }
}
