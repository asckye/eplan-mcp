namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.DeviceList"/> (docs/tools/eplan_device_list.md). The filters combine with
/// "and"; null or blank means no filter.
/// </summary>
public sealed class DeviceListArgs : ListArgs
{
    /// <summary>Text the full tag or the shown tag (without '&gt;' markers) contains, ignoring case.</summary>
    public string? TagContains { get; set; }

    /// <summary>Full name of the page the functions must be placed on.</summary>
    public string? Page { get; set; }

    /// <summary>Name of an EPLAN function category (<c>Base.Enums.FunctionCategory</c>); the Bridge checks it.</summary>
    public string? Category { get; set; }

    /// <summary>True: only functions with part references; false: only functions without; null: both.</summary>
    public bool? HasPart { get; set; }

    /// <summary>Whether auxiliary functions (contacts, connection points, …) are listed besides the main functions.</summary>
    public bool IncludeAuxiliary { get; set; }
}
