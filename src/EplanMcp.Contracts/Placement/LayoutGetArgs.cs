namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.LayoutGet"/> (docs/tools/eplan_layout_get.md); the slice is of the placed objects.</summary>
public sealed class LayoutGetArgs : ListArgs
{
    /// <summary>One installation space by its designation (36452, e.g. <c>UC1</c>, A136), or null for all.</summary>
    public string? Space { get; set; }
}
