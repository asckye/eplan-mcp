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
}
