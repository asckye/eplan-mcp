namespace EplanMcp.Coverage.Mapping;

/// <summary>Reviewed executable aliases missed by the scan; only the named overloads and tools count.</summary>
internal sealed class ToolReach
{
    public string Match { get; set; } = "";

    public List<string> Members { get; set; } = [];

    public List<string> Tools { get; set; } = [];

    public string Via { get; set; } = "";

    public string Source { get; set; } = "";

    public string Receiver { get; set; } = "";
}
