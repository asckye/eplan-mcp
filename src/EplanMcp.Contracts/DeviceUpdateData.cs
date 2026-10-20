namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.DeviceUpdateExecute"/>: the renamed device as read back. Tags are project data.</summary>
public sealed class DeviceUpdateData
{
    /// <summary>The name of the project the device is in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The main function's full tag now.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>The full tag before.</summary>
    public string PreviousTag { get; set; } = string.Empty;

    /// <summary>The tag the page shows now, without '&gt;' markers; null when it shows none.</summary>
    public string? VisibleTag { get; set; }

    /// <summary>How many functions (the main and the auxiliary ones) carry the new tag.</summary>
    public int Functions { get; set; }

    /// <summary>The pages those functions are on, in EPLAN's order, at most 20.</summary>
    public List<string> Pages { get; set; } = [];

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}
