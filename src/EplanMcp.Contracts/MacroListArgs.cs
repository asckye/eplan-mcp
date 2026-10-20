namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.MacroList"/> (docs/tools/eplan_macro_list.md). Macros belong to EPLAN's
/// settings, not to a project, so unlike <see cref="ListArgs"/> there is no project.
/// </summary>
public sealed class MacroListArgs
{
    /// <summary>Text the macro's path below the macro directory contains, ignoring case; null or blank means every macro.</summary>
    public string? NameContains { get; set; }

    /// <summary>Index of the first macro to return, taken from the Host's cursor.</summary>
    public int Offset { get; set; }

    /// <summary>The most macros to return.</summary>
    public int Limit { get; set; }
}
