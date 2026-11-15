namespace EplanMcp.Contracts;

/// <summary>Result of plc.rack.execute (docs/tools/eplan_plc_rack.md, section 5).</summary>
public sealed class PlcRackData
{
    public string Project { get; set; } = string.Empty;

    public string RackPage { get; set; } = string.Empty;

    /// <summary>The drawn cards, left to right.</summary>
    public List<PlcRackCardResult> Cards { get; set; } = [];

    /// <summary>Layout or readback findings that did not stop the commit; null when none.</summary>
    public string? Warning { get; set; }
}

/// <summary>One drawn card: its device, pages and the parts on its main function after the commit.</summary>
public sealed class PlcRackCardResult
{
    public string Tag { get; set; } = string.Empty;

    public string OverviewPage { get; set; } = string.Empty;

    /// <summary>The picture's insertion x on the rack page, in millimetres.</summary>
    public double RackX { get; set; }

    /// <summary>The picture's width, in millimetres.</summary>
    public double Width { get; set; }

    /// <summary>The part numbers on the card's main function, as read back.</summary>
    public List<string> Parts { get; set; } = [];

    /// <summary>The sub-devices' new tags.</summary>
    public List<string> SubDevices { get; set; } = [];
}
