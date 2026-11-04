namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.MacroInsertPlan"/> and <see cref="OperationNames.MacroInsertExecute"/>.</summary>
public sealed class MacroInsertArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full name of the Circuit or Overview page to insert on.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The macro's path below the macro directory, as eplan_macro_list returns it.</summary>
    public string? Macro { get; set; }

    /// <summary>Part number whose group symbol macro is inserted; mutually exclusive with Macro.</summary>
    public string? Part { get; set; }

    /// <summary>Part variant; defaults to 1 or the part's unique variant.</summary>
    public string? PartVariant { get; set; }

    /// <summary>MultiLine (default) or Overview, matching the target page type.</summary>
    public string? Representation { get; set; }

    /// <summary>The insertion point's x, in millimetres from the page's left edge: the macro outline's left side.</summary>
    public double X { get; set; }

    /// <summary>The insertion point's y, in millimetres up from the page's bottom edge: the macro outline's top side.</summary>
    public double Y { get; set; }

    /// <summary>The macro variant of the requested representation, as eplan_macro_get lists them.</summary>
    public int Variant { get; set; }

    /// <summary>
    /// <c>number</c> (the default): EPLAN gives the macro's devices tags no device has; <c>keep</c>: the macro's own
    /// tags in the page's structure.
    /// </summary>
    public string? Numbering { get; set; }

    /// <summary>Retain the macro box according to project settings; false removes inserted boxes in the same transaction.</summary>
    public bool MacroBox { get; set; } = true;

    /// <summary>Use warn to reproduce existing drawings; refuse is the default.</summary>
    public string? LayoutGuard { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}
