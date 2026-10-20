namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What <see cref="IDeviceRenamer.Rename"/> reads back after the rename, inside the transaction.</summary>
public sealed class RenameReadBack
{
    /// <summary>The main function's <c>Name</c> now; EPLAN drops structure parts the category does not use (A13).</summary>
    public string MainName { get; set; } = string.Empty;

    /// <summary>The main function's <c>VisibleName</c> as EPLAN returns it.</summary>
    public string VisibleName { get; set; } = string.Empty;

    /// <summary>Functions still named exactly the old tag; anything but 0 means part of the device was left behind.</summary>
    public int Remaining { get; set; }

    /// <summary>Functions named exactly the main function's new name.</summary>
    public int Renamed { get; set; }
}
