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
}
