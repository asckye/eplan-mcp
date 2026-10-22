namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ImportPlcPlan"/> and <see cref="OperationNames.ImportPlcExecute"/>.</summary>
public sealed class ImportPlcArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The file's path below the Host's import folder (SEC-31).</summary>
    public string? File { get; set; }

    /// <summary>The full path resolved by the Host below its import root; never shown to the model (SEC-12).</summary>
    public string? FilePath { get; set; }

    /// <summary>The Host's SHA-256, lowercase hex; the Bridge reads and checks it again.</summary>
    public string? Sha256 { get; set; }

    /// <summary>PlcDcXMLExchangerUniversal for .pbf, or PlcDcAMLExchangerGeneral for .aml (S34, api-notes A39).</summary>
    public string? Converter { get; set; }

    /// <summary>For execute: the plan's <see cref="WritePlan.Fingerprint"/>; null for plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For execute: where to back the project up first (ADR-0013); null for plan.</summary>
    public BackupTarget? Backup { get; set; }
}
