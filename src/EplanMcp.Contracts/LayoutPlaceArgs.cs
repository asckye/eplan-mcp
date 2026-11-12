namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.LayoutPlacePlan"/> and <see cref="OperationNames.LayoutPlaceExecute"/>.</summary>
public sealed class LayoutPlaceArgs
{
    /// <summary>Project name or path; required for a write.</summary>
    public string? Project { get; set; }

    /// <summary>The installation space designation holding the enclosure, e.g. <c>UC1</c>.</summary>
    public string? Space { get; set; }

    /// <summary>1-100 items, placed in this order; a device may sit on a rail placed earlier in the same call.</summary>
    public List<LayoutPlaceItem> Items { get; set; } = [];

    /// <summary>For the execution: the fingerprint the plan returned.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>One rail, duct or device to place.</summary>
public sealed class LayoutPlaceItem
{
    /// <summary><c>rail</c>, <c>duct</c> or <c>device</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>A name later items use in <see cref="Rail"/> to sit on this rail.</summary>
    public string? Ref { get; set; }

    /// <summary>Rail or duct: the part number. Device: optional, default the device's first part.</summary>
    public string? Part { get; set; }

    /// <summary>The part variant; default <c>1</c>.</summary>
    public string? Variant { get; set; }

    /// <summary>Rail or duct length in millimetres.</summary>
    public double? LengthMm { get; set; }

    /// <summary><c>horizontal</c> (default) or <c>vertical</c>, for rails and ducts.</summary>
    public string? Orientation { get; set; }

    /// <summary>Device: the schematic device tag.</summary>
    public string? Tag { get; set; }

    /// <summary><c>panel</c> (default for rails and ducts), <c>door</c>, or <c>rail</c> (devices only).</summary>
    public string? On { get; set; }

    /// <summary>On a rail: an earlier item's <see cref="Ref"/>, or the object ID of a placed rail from eplan_layout_get.</summary>
    public string? Rail { get; set; }

    /// <summary>On a rail: the device's left edge from the rail's left end, millimetres.</summary>
    public double? OffsetMm { get; set; }

    /// <summary>On a panel or door: the item's lower left corner from the surface's lower left corner, millimetres.</summary>
    public double? X { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public double? Y { get; set; }
}
