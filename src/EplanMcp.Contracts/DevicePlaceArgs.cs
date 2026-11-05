namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.DevicePlacePlan"/> and <see cref="OperationNames.DevicePlaceExecute"/>.</summary>
public sealed class DevicePlaceArgs
{
    /// <summary>Use warn to reproduce existing drawings; refuse is the default.</summary>
    public string? LayoutGuard { get; set; }

    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full name of the circuit page to place the device on, for example <c>=GAA+A1&amp;EFS1/60</c>.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The part number in EPLAN's parts database, for example <c>SIE.3RH2122-1HB40</c>.</summary>
    public string? Part { get; set; }

    /// <summary>The part's variant; null for variant 1, or for the part's only variant.</summary>
    public string? Variant { get; set; }

    /// <summary>Project symbol library; can override a part's template symbol.</summary>
    public string? Library { get; set; }
    /// <summary>Exact symbol name from the project library.</summary>
    public string? Symbol { get; set; }
    /// <summary>Native variant number; without library/symbol a part retains its template symbol and changes only the variant.</summary>
    public int? SymbolVariant { get; set; }
    /// <summary>Exact localized property arrangement name; omitted preserves the symbol default.</summary>
    public string? PropertyArrangement { get; set; }
    /// <summary>Requested property placements relative to the symbol (A53/A54).</summary>
    public List<DeviceDisplayProperty>? DisplayProperties { get; set; }
    /// <summary>One designation per connection point in native array order (A55).</summary>
    public string[]? ConnectionDesignations { get; set; }
    /// <summary>One description per connection point in native array order (20023); a PLC point shows it as 20029.</summary>
    public string[]? ConnectionDescriptions { get; set; }
    /// <summary>Exclusive location-box source: a literal location identifier such as +P1 (A55).</summary>
    public string? LocationBox { get; set; }
    /// <summary>Opposite corner x in millimetres; required for LocationBox or SPECIAL/DC.</summary>
    public double? CornerX { get; set; }
    /// <summary>Opposite corner y in millimetres; required for LocationBox or SPECIAL/DC.</summary>
    public double? CornerY { get; set; }
    /// <summary>Parent strip designation, short or full; also supported for a part terminal.</summary>
    public string? TerminalStrip { get; set; }
    /// <summary>Single terminal designation, appended to the strip with ':'.</summary>
    public string? TerminalNumber { get; set; }
    /// <summary>Optional language-independent function text, only when creating the parent strip.</summary>
    public string? TerminalStripText { get; set; }
    /// <summary>Optional absolute [x,y] mm for a new strip definition; requires TerminalStripText.</summary>
    public double[]? TerminalStripPosition { get; set; }

    /// <summary>Internal plugin default for a new placed strip definition; never a public tool parameter.</summary>
    public StripDefinitionSymbol? StripDefinition { get; set; }

    /// <summary>PLC pin designation; with an explicit plug the suffix is plug:point. Tag identifies its existing card.</summary>
    public string? PlcConnectionPoint { get; set; }
    /// <summary>PLC I/O address (20400), optional with PlcConnectionPoint.</summary>
    public string? PlcAddress { get; set; }
    /// <summary>Manual symbolic address (20402), optional with PlcConnectionPoint.</summary>
    public string? PlcSymbolicAddress { get; set; }
    /// <summary>Language-independent PLC function text (20011), optional with PlcConnectionPoint.</summary>
    public string? PlcText { get; set; }

    /// <summary>Optional manual PLC plug designation (20406); requires a complete PLC point source.</summary>
    public string? PlcPlugDesignation { get; set; }

    /// <summary>The insertion point's x, in millimetres from the page's left edge.</summary>
    public double X { get; set; }

    /// <summary>The insertion point's y, in millimetres up from the page's bottom edge.</summary>
    public double Y { get; set; }

    /// <summary>Short or full device tag; required for symbols, optional for parts to retain EPLAN numbering.</summary>
    public string? Tag { get; set; }

    /// <summary>Join an existing device with a secondary library symbol; omitted means false.</summary>
    public bool? JoinDevice { get; set; }

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }
}
