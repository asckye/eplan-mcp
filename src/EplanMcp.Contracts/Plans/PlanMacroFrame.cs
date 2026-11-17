namespace EplanMcp.Contracts;

/// <summary>Macro outline relative to its top-left insertion point, millimetres, y up (A14).</summary>
public sealed class PlanMacroFrame
{
    /// <summary>Left edge, zero.</summary>
    public double Left { get; set; }

    /// <summary>Top edge, zero.</summary>
    public double Top { get; set; }

    /// <summary>Right edge, the outline width.</summary>
    public double Right { get; set; }

    /// <summary>Bottom edge, the negative outline height.</summary>
    public double Bottom { get; set; }
}
