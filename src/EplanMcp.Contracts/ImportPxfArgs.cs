namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ImportPxfPlan"/> and of its execute operation, which imports the file as a new project.</summary>
public sealed class ImportPxfArgs
{
    /// <summary>The EPJ (PXF) file's path below the import folder, with <c>/</c> between folders, as the tool returns it.</summary>
    public string? File { get; set; }

    /// <summary>
    /// The file's full path, which the Host resolved below its import root by the rules of SEC-31; never shown to the
    /// model (SEC-12).
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>The SHA-256 of the file as the Host read it, lowercase hex.</summary>
    public string? Sha256 { get; set; }

    /// <summary>The new project's name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The new project's full path without extension, which the Host chose in its first scratch root:
    /// <c>&lt;scratch&gt;\imported\&lt;name&gt;</c> (docs/tools/eplan_import_pxf.md, section 5; api-notes A29).
    /// </summary>
    public string? Target { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}
