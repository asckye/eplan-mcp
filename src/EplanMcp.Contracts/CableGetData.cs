namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.CableGet"/>.</summary>
public sealed class CableGetData
{
    /// <summary>The name of the project actually used, so the tool can echo it (mcp-tool-design 7.1).</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The cable itself.</summary>
    public CableInfo Cable { get; set; } = new();

    /// <summary>The requested slice of the cores (<c>Cable.CableConnections</c>) in EPLAN's order, each with its designation.</summary>
    public List<ConnectionListItem> Cores { get; set; } = [];

    /// <summary>How many cores the cable has.</summary>
    public int Total { get; set; }
}
