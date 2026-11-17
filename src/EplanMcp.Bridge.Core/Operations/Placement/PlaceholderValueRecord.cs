namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One variable's value in one value set of a placeholder. Project data.</summary>
public sealed class PlaceholderValueRecord
{
    public string ValueSet { get; set; } = string.Empty;

    public string Variable { get; set; } = string.Empty;

    public PlaceholderText Value { get; set; } = new();
}
