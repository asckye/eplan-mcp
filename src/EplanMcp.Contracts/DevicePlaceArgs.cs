namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.DevicePlacePlan"/> and <see cref="OperationNames.DevicePlaceExecute"/>.</summary>
public sealed class DevicePlaceArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full name of the circuit page to place the device on, for example <c>=GAA+A1&amp;EFS1/60</c>.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The part number in EPLAN's parts database, for example <c>SIE.3RH2122-1HB40</c>.</summary>
    public string? Part { get; set; }

    /// <summary>The part's variant; null for variant 1, or for the part's only variant.</summary>
    public string? Variant { get; set; }

    /// <summary>Project symbol library; mutually exclusive with Part and Variant.</summary>
    public string? Library { get; set; }
    /// <summary>Exact symbol name from the project library.</summary>
    public string? Symbol { get; set; }
    /// <summary>Native variant number, required for a symbol source.</summary>
    public int? SymbolVariant { get; set; }
    /// <summary>Requested property placements relative to the symbol (A53/A54).</summary>
    public List<DeviceDisplayProperty>? DisplayProperties { get; set; }
    /// <summary>One designation per connection point in native array order (A55).</summary>
    public string[]? ConnectionDesignations { get; set; }

    /// <summary>The insertion point's x, in millimetres from the page's left edge.</summary>
    public double X { get; set; }

    /// <summary>The insertion point's y, in millimetres up from the page's bottom edge.</summary>
    public double Y { get; set; }

    /// <summary>The device's tag: from '-' (<c>-KF20</c>) for the page's structure, or in full (<c>=GAA+A1-KF20</c>).</summary>
    public string? Tag { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}
