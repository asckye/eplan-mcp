namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ImportPxfExecute"/>: the new project, which EPLAN has not opened.</summary>
public sealed class ImportPxfData
{
    /// <summary>The new project's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The new project's link file (<c>.elk</c>), in the scratch folder.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>How long the import took, in whole seconds (S22: 58 for a 598 MB file).</summary>
    public int Seconds { get; set; }
}
