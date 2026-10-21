namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ImportDxfPlan"/> and of its execute operation, which imports the drawing.</summary>
public sealed class ImportDxfArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full name of the page to import the drawing on, for example <c>=GAA+A1&amp;EFS1/92</c>.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The file's path below the import folder, with <c>/</c> between folders, as the tool returns it.</summary>
    public string? File { get; set; }

    /// <summary>
    /// The file's full path, which the Host resolved below its import root by the rules of SEC-31; never shown to the
    /// model (SEC-12).
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>The SHA-256 of the file as the Host read it, lowercase hex; the Bridge reads the file again and compares.</summary>
    public string? Sha256 { get; set; }

    /// <summary><c>fit</c> (the default): scaled to the page; <c>none</c>: in its own size.</summary>
    public string? Scale { get; set; }

    /// <summary><c>center</c> (the default): in the middle of the page; <c>lower_left</c>: at its lower left corner.</summary>
    public string? Position { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}
