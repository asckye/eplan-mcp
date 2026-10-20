using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Checks every write operation makes before it plans or executes.</summary>
public static class WriteArguments
{
    /// <summary>Writes never fall back to the project selected in EPLAN (mcp-tool-design 5).</summary>
    public static void RequireProject(string? project, List<string> violations)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }
    }

    /// <summary>All argument problems at once as INVALID_ARGUMENT, in the shape of the Host's own (mcp-tool-design 6.2).</summary>
    public static BridgeErrorException Invalid(IReadOnlyList<string> violations) => new(
        ErrorReason.InvalidArgument,
        "Invalid arguments: " + string.Join(" ", violations),
        retryable: false,
        new Dictionary<string, string> { ["violations"] = string.Join("\n", violations) });

    /// <summary>The project named by <paramref name="project"/>, refused when EPLAN has it open read-only.</summary>
    public static OpenProject Writable(IProjectCatalog catalog, string? project)
    {
        OpenProject open = ProjectResolver.Resolve(catalog.ListOpenProjects(), project);
        return open.ReadOnly
            ? throw new BridgeErrorException(ErrorReason.InvalidArgument, "EPLAN has this project open read-only, so it cannot be changed. Ask the engineer to open it for writing.")
            : open;
    }

    /// <summary>Marks an error found before the transaction: nothing was changed (ADR-0010).</summary>
    public static BridgeErrorException NotStarted(BridgeErrorException error)
    {
        error.Error.Execution = ExecutionState.NotStarted;
        return error;
    }
}
