namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.ConnectionList"/> (docs/tools/eplan_connection_list.md). At least one of
/// <see cref="Device"/> and <see cref="Page"/> is needed; with both, only connections meeting both are listed. Null or
/// blank means not given.
/// </summary>
public sealed class ConnectionListArgs : ListArgs
{
    /// <summary>A device tag as eplan_device_get takes it: the full tag, or a shown tag such as <c>-FC1</c> that one device has.</summary>
    public string? Device { get; set; }

    /// <summary>Full name of the page the connections are placed on, matched exactly.</summary>
    public string? Page { get; set; }

    /// <summary>Include connection locators for detailed output.</summary>
    public bool Detailed { get; set; }
}
