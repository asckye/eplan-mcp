namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What <see cref="IDevicePlacer.Place"/> reads back after the rename, inside the transaction.</summary>
public sealed class PlacedDevice
{
    /// <summary>The name of every function <c>CreateDevice</c> returned, main function first; none when it placed nothing.</summary>
    public List<string> Names { get; set; } = [];

    /// <summary>The main function's <c>Name</c>; empty without a main function.</summary>
    public string MainName { get; set; } = string.Empty;

    /// <summary>The main function's <c>VisibleName</c> as EPLAN returns it.</summary>
    public string VisibleName { get; set; } = string.Empty;

    /// <summary>The main function's category, for example <c>Coil</c>.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>How many of the functions are on no page (<c>IsPlaced</c> false; S20: the 4 contacts of a contactor).</summary>
    public int Unplaced { get; set; }

    /// <summary>How many functions are on the page that were not there before.</summary>
    public int OnPage { get; set; }
}
