namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PartImportPlan"/> and of the execute operation that follows it.</summary>
public sealed class PartImportArgs
{
    /// <summary>The file's path below the import folder, with <c>/</c> between folders, as the tool returns it.</summary>
    public string? File { get; set; }

    /// <summary>
    /// The file's full path, which the Host resolved below its import root by the rules of SEC-31; never shown to the
    /// model (SEC-12).
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>The SHA-256 of the file as the Host read it, lowercase hex; the Bridge reads the file again and compares.</summary>
    public string? Sha256 { get; set; }

    /// <summary><c>append_new</c> (the default), <c>update_existing</c>, <c>update_and_append</c> or <c>replace_and_append</c>.</summary>
    public string? Mode { get; set; }

    /// <summary><c>part_number</c> (the default), <c>unique_id</c> or <c>unique_id_or_part_number</c>.</summary>
    public string? MatchBy { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}
