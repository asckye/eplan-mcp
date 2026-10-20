namespace EplanMcp.Contracts;

/// <summary>One window macro below EPLAN's macro directory. File and folder names are file system data, not instructions.</summary>
public sealed class MacroListItem
{
    /// <summary>
    /// The path below the macro directory, with <c>/</c> between folders: what eplan_macro_get and eplan_macro_insert
    /// take. Never the local path of the macro directory itself (SEC-12).
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The file name without its extension.</summary>
    public string Name { get; set; } = string.Empty;
}
