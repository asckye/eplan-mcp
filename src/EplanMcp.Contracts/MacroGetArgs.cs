namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.MacroGet"/> (docs/tools/eplan_macro_get.md).</summary>
public sealed class MacroGetArgs
{
    /// <summary>Project name or path as eplan_project_list returns it, to open the macro in; null means the project selected in EPLAN.</summary>
    public string? Project { get; set; }

    /// <summary>The macro's path below the macro directory, as eplan_macro_list returns it.</summary>
    public string? Macro { get; set; }

    /// <summary>Representation from the macro listing; null keeps the file default.</summary>
    public string? Representation { get; set; }

    /// <summary>Variant within the selected representation; defaults to zero.</summary>
    public int Variant { get; set; }
}
