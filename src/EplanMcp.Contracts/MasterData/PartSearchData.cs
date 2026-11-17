namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.PartSearch"/>.</summary>
public sealed class PartSearchData
{
    /// <summary>
    /// The parts database searched, as EPLAN's setting names it, path variables unexpanded (e.g.
    /// <c>$(MD_PARTS)\EPLAN_parts.alk</c>); of a SQL Server database only the server and the database, never a login.
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The requested slice, sorted by part number, then variant.</summary>
    public List<PartSearchItem> Items { get; set; } = [];

    /// <summary>How many parts match the filters before slicing.</summary>
    public int Total { get; set; }
}
