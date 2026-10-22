namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ProjectCreateExecute"/>: the new project, which is not left open.</summary>
public sealed class NewProjectData
{
    /// <summary>The new project's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The new project's link file (<c>.elk</c>).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>A created project's page count, read in the same call (S27: 1 for GB_bas001); null for a restored one, which is not opened.</summary>
    public int? Pages { get; set; }

    /// <summary>A created project's <c>PROJ_GUID</c>, <c>&lt;template&gt;@&lt;unix-seconds&gt;</c> (api-notes A34); null for a restored one.</summary>
    public string? ProjectGuid { get; set; }

    /// <summary>A restored project's database version read without opening it (A31: <c>18.0</c>); null for a created one.</summary>
    public string? DatabaseVersion { get; set; }

    /// <summary>How long EPLAN took, in whole seconds (S27: 3.5 to create, 6 to restore the sample project).</summary>
    public int Seconds { get; set; }
}
