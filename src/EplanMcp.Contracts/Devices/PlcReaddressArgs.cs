namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PlcReaddressPlan"/> and <see cref="OperationNames.PlcReaddressExecute"/> (docs/tools/eplan_plc_readdress.md).</summary>
public sealed class PlcReaddressArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The first address of each kind; empty leaves that kind to EPLAN's default.</summary>
    public string? DigitalInput { get; set; }

    public string? DigitalOutput { get; set; }

    public string? AnalogInput { get; set; }

    public string? AnalogOutput { get; set; }

    /// <summary>Limits the call to one CPU; empty means all.</summary>
    public string? Cpu { get; set; }

    /// <summary>The order addresses are given in: connection_point, channel or graphical.</summary>
    public string? Sorting { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Where the Bridge copies the project first; the Host's pipeline fills it in.</summary>
    public BackupTarget? Backup { get; set; }
}

/// <summary>A PLC connection point and its address.</summary>
public sealed class PlcAddressEntry
{
    /// <summary>The object's identity within the project, which stays when its address changes.</summary>
    public string Id { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;
}

/// <summary>One point whose address changed.</summary>
public sealed class PlcAddressChange
{
    public string Tag { get; set; } = string.Empty;

    public string Previous { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;
}

/// <summary>Data of <see cref="OperationNames.PlcReaddressExecute"/>.</summary>
public sealed class PlcReaddressData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>The first points that changed, at most 200.</summary>
    public List<PlcAddressChange> Changed { get; set; } = [];

    public int ChangedCount { get; set; }

    /// <summary>The path of the backup copy.</summary>
    public string? Backup { get; set; }

    public string? Warning { get; set; }
}
