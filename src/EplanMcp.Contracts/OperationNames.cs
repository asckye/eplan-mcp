namespace EplanMcp.Contracts;

/// <summary>
/// Names of Bridge operations carried by <c>invoke</c>, formatted <c>&lt;resource&gt;.&lt;action&gt;</c>.
/// New capabilities add operations here, not new JSON-RPC methods (ADR-0003).
/// </summary>
public static class OperationNames
{
    /// <summary>Lists the projects open in EPLAN. Arguments: <see cref="ProjectListArgs"/>; data: <see cref="ProjectListData"/>.</summary>
    public const string ProjectList = "project.list";

    /// <summary>Overview of one project: counts, pages per type, optionally its properties. Arguments: <see cref="ProjectGetArgs"/>; data: <see cref="ProjectGetData"/>.</summary>
    public const string ProjectGet = "project.get";

    /// <summary>Pages of one project, filtered and paged in the Bridge. Arguments: <see cref="PageListArgs"/>; data: <see cref="ListData{TItem}"/> of <see cref="PageSummary"/>.</summary>
    public const string PageList = "page.list";

    /// <summary>One page by its full name: type, description, devices, optionally its properties. Arguments: <see cref="PageGetArgs"/>; data: <see cref="PageGetData"/>.</summary>
    public const string PageGet = "page.get";

    /// <summary>
    /// Lists the devices of a project, filtered and sliced in the Bridge. Arguments: <see cref="DeviceListArgs"/>;
    /// data: <see cref="ListData{TItem}"/> of <see cref="DeviceListItem"/>.
    /// </summary>
    public const string DeviceList = "device.list";

    /// <summary>One device by tag. Arguments: <see cref="DeviceGetArgs"/>; data: <see cref="DeviceGetData"/>.</summary>
    public const string DeviceGet = "device.get";

    /// <summary>
    /// Lists the connections of a device, of a page, or of both, sliced in the Bridge. Arguments:
    /// <see cref="ConnectionListArgs"/>; data: <see cref="ListData{TItem}"/> of <see cref="ConnectionListItem"/>.
    /// </summary>
    public const string ConnectionList = "connection.list";

    /// <summary>
    /// Lists the parts a project uses, summed up per part number and variant, filtered and sliced in the Bridge.
    /// Arguments: <see cref="PartListArgs"/>; data: <see cref="ListData{TItem}"/> of <see cref="PartListItem"/>.
    /// </summary>
    public const string PartList = "part.list";
}
