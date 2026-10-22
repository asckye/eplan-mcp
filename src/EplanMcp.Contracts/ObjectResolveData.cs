namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ObjectResolve"/>: one result per locator, in the order given.</summary>
public sealed class ObjectResolveData
{
    /// <summary>The project's name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The project's path without extension.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>The results.</summary>
    public List<LocatorResult> Results { get; set; } = [];
}
