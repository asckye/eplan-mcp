namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What <see cref="IPlaceholderWriter.Apply"/> read inside the transaction: the current values before, and the placeholder after.</summary>
public sealed class PlaceholderApplied
{
    /// <summary>The placeholder's current values just before the value set was applied.</summary>
    public List<PlaceholderText> Before { get; set; } = [];

    /// <summary>The placeholder read again after, with its current values.</summary>
    public PlaceholderRecord After { get; set; } = new();
}
