namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ProjectCompatibility"/>, read without opening the project (api-notes A31).</summary>
public sealed class ProjectCompatibilityData
{
    /// <summary>EPLAN's <c>DatabaseVersion.Status</c> name; <c>IsCompatible</c> for a project of this EPLAN version (A31).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Status"/> is <c>IsCompatible</c>: only then may the Bridge open it read-only.</summary>
    public bool Compatible { get; set; }

    /// <summary>The project's database version, for example <c>18.0</c>.</summary>
    public string DatabaseVersion { get; set; } = string.Empty;

    /// <summary>Whether the project is open in EPLAN.</summary>
    public bool Open { get; set; }
}
