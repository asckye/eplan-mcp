namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.PlcGet"/>.</summary>
public sealed class PlcGetData
{
    /// <summary>The name of the project actually used, so the tool can echo it (mcp-tool-design 7.1).</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The PLC box itself.</summary>
    public PlcInfo Plc { get; set; } = new();

    /// <summary>The requested slice of the channels (<c>PLC.PLCTerminals</c>), in EPLAN's order.</summary>
    public List<PlcChannel> Channels { get; set; } = [];

    /// <summary>How many channels the PLC box has.</summary>
    public int Total { get; set; }

    /// <summary>How many of all its channels have a connection.</summary>
    public int Connected { get; set; }
}
