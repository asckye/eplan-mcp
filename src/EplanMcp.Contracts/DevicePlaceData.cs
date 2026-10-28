namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.DevicePlaceExecute"/>: the placed device as read back. Tags are project data.</summary>
public sealed class DevicePlaceData
{
    /// <summary>Native terminal identity, ownership and position; null for other sources.</summary>
    public TerminalPlacementData? Terminal { get; set; }
    /// <summary>Location-box readback, or null for device sources.</summary>
    public LocationBoxData? LocationBox { get; set; }
    /// <summary>The name of the project the device was placed in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The full name of the page the device was placed on.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The part number as the parts database has it.</summary>
    public string Part { get; set; } = string.Empty;

    /// <summary>The part's variant that was placed.</summary>
    public string Variant { get; set; } = string.Empty;

    /// <summary>The main function's full tag now.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>The tag the page shows, without '&gt;' markers; null when it shows none.</summary>
    public string? VisibleTag { get; set; }

    /// <summary>The main function's category, for example <c>Coil</c>.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>How many functions the device has: the main one and the auxiliary ones.</summary>
    public int Functions { get; set; }

    /// <summary>How many of them are on no page yet (spike S20: the contacts of a contactor).</summary>
    public int Unplaced { get; set; }

    /// <summary>False when EPLAN could not read the page's size (a new, empty page), so the point was not checked against it.</summary>
    public bool PageSizeKnown { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}
