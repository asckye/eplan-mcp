namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.MacroInsertPlan"/> and <see cref="OperationNames.MacroInsertExecute"/>.</summary>
public sealed class MacroInsertArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full name of the circuit page to insert on, for example <c>=GAA+A1&amp;EFS1/60</c>.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The macro's path below the macro directory, as eplan_macro_list returns it.</summary>
    public string? Macro { get; set; }

    /// <summary>The insertion point's x, in millimetres from the page's left edge: the macro outline's left side.</summary>
    public double X { get; set; }

    /// <summary>The insertion point's y, in millimetres up from the page's bottom edge: the macro outline's top side.</summary>
    public double Y { get; set; }

    /// <summary>The macro variant of its <c>MultiLine</c> representation, as eplan_macro_get lists them.</summary>
    public int Variant { get; set; }

    /// <summary>
    /// <c>number</c> (the default): EPLAN gives the macro's devices tags no device has; <c>keep</c>: the macro's own
    /// tags in the page's structure.
    /// </summary>
    public string? Numbering { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}
