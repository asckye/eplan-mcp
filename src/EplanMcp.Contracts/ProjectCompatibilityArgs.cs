namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ProjectCompatibility"/>.</summary>
public sealed class ProjectCompatibilityArgs
{
    /// <summary>The full local path of a project's .elk file, open or not.</summary>
    public string? Path { get; set; }
}
