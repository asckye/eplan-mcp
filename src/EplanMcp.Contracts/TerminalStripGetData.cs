namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.TerminalStripGet"/>.</summary>
public sealed class TerminalStripGetData
{
    /// <summary>The name of the project actually used, so the tool can echo it (mcp-tool-design 7.1).</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The strip with its counts.</summary>
    public TerminalStripListItem Strip { get; set; } = new();

    /// <summary>The requested slice of the terminals, in the strip's order (<c>TerminalStrip.Terminals</c>).</summary>
    public List<TerminalItem> Terminals { get; set; } = [];

    /// <summary>How many terminals the strip has.</summary>
    public int Total { get; set; }
}
