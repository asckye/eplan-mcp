namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.MacroGet"/> (docs/tools/eplan_macro_get.md).</summary>
public sealed class MacroGetArgs
{
    /// <summary>Project name or path as eplan_project_list returns it, to open the macro in; null means the project selected in EPLAN.</summary>
    public string? Project { get; set; }

    /// <summary>The macro's path below the macro directory, as eplan_macro_list returns it.</summary>
    public string? Macro { get; set; }
}
