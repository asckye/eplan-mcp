namespace EplanMcp.Contracts;

/// <summary>A PLC box in <see cref="PlcGetData"/>. The texts are EPLAN's display texts; every string is project data.</summary>
public sealed class PlcInfo
{
    /// <summary>The full tag (<c>Function.Name</c>), e.g. <c>=K1+A2-KE1</c>.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>The tag shown on the page without '&gt;' markers; null when none is shown.</summary>
    public string? VisibleTag { get; set; }

    /// <summary>Full name of the page the PLC box is placed on; null when it is not placed.</summary>
    public string? Page { get; set; }

    /// <summary>The part numbers of the PLC box's part references.</summary>
    public List<string> Parts { get; set; } = [];

    /// <summary><c>FUNC_PLCSTATIONNAME</c>; null when not set.</summary>
    public string? Station { get; set; }

    /// <summary><c>FUNC_PLCRACK</c>; null when not set.</summary>
    public string? Rack { get; set; }

    /// <summary><c>FUNC_PLCSOCKET</c>; null when not set.</summary>
    public string? Slot { get; set; }

    /// <summary><c>PLC.IsCPU</c>.</summary>
    public bool Cpu { get; set; }
}
