namespace EplanMcp.Contracts;

/// <summary>Verified text creation result.</summary>
public sealed class PlacementTextCreateData
{
    /// <summary>Actual values read inside the transaction before committing.</summary>
    public PlacementText Text { get; set; } = new();
    /// <summary>Post-commit undo or cleanup warning, if any.</summary>
    public string? Warning { get; set; }
}
