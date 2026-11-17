namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.MasterDataUpdatePlan"/> and <see cref="OperationNames.MasterDataUpdateExecute"/> (docs/tools/eplan_masterdata_update.md).</summary>
public sealed class MasterDataUpdateArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Where the Bridge copies the project first; the Host's pipeline fills it in.</summary>
    public BackupTarget? Backup { get; set; }
}

/// <summary>Data of <see cref="OperationNames.MasterDataUpdateExecute"/>.</summary>
public sealed class MasterDataUpdateData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>How many master data files the project stores afterwards.</summary>
    public int Entries { get; set; }

    /// <summary>The files the project did not store before.</summary>
    public List<string> Added { get; set; } = [];

    /// <summary>The path of the backup copy.</summary>
    public string? Backup { get; set; }
}
