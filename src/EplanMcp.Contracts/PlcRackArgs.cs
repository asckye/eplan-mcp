namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of plc.rack.plan and plc.rack.execute (docs/tools/eplan_plc_rack.md, section 3): the cards of one PLC rack,
/// left to right. Each card is drawn first as its macro's Overview representation on its own overview page, then as the
/// macro's multi-line picture on the rack page, the pictures side by side (A122).
/// </summary>
public sealed class PlcRackArgs
{
    /// <summary>Name or path of an open project.</summary>
    public string? Project { get; set; }

    /// <summary>Full name of the rack page: an existing, empty Circuit page.</summary>
    public string? RackPage { get; set; }

    /// <summary>The cards from left to right, 1 to 20.</summary>
    public List<PlcRackCard>? Cards { get; set; }

    /// <summary>Insertion point of the first picture, [x, y] in millimetres from the bottom left; default [24, 264].</summary>
    public double[]? Origin { get; set; }

    /// <summary>Insertion point of each card on its overview page, [x, y]; default [176, 264].</summary>
    public double[]? OverviewPosition { get; set; }

    /// <summary>The fingerprint of the preview the execution must still match.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>One card of <see cref="PlcRackArgs"/>.</summary>
public sealed class PlcRackCard
{
    /// <summary>The card's full device tag, such as =K1+UC1-KE3; no function may have it yet.</summary>
    public string? Tag { get; set; }

    /// <summary>The card's part number; the part must name a window macro.</summary>
    public string? Part { get; set; }

    /// <summary>The part variant; default the parts database's first.</summary>
    public string? PartVariant { get; set; }

    /// <summary>Full name of the card's overview page: an existing, empty Overview page.</summary>
    public string? OverviewPage { get; set; }

    /// <summary>The multi-line variant of the rack picture (A122: DI/DQ light 5, dark 6, F-DI 6, CPU 1, CM PtP 3).</summary>
    public int? PictureVariant { get; set; }

    /// <summary>The Overview variant; default 0.</summary>
    public int? OverviewVariant { get; set; }

    /// <summary>Further parts on the card's main function, such as its BaseUnit.</summary>
    public List<string>? ExtraParts { get; set; }

    /// <summary>Sub-devices of the macro to rename and give a part, such as a CPU's bus adapter.</summary>
    public List<PlcRackSubDevice>? SubDevices { get; set; }
}

/// <summary>A sub-device of a card's macro: the tag suffix it has in the macro, its new full tag and its part.</summary>
public sealed class PlcRackSubDevice
{
    /// <summary>The suffix after the card's macro tag, such as -K1 for the macro's -K1-K1.</summary>
    public string? MacroSuffix { get; set; }

    /// <summary>The sub-device's new full tag.</summary>
    public string? Tag { get; set; }

    /// <summary>The sub-device's part number, if any.</summary>
    public string? Part { get; set; }
}
