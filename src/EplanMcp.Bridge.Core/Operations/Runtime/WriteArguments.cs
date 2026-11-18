using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Checks every write operation makes before it plans or executes.</summary>
public static class WriteArguments
{
    /// <summary>Writes never fall back to the project selected in EPLAN (the MCP server standard).</summary>
    public static void RequireProject(string? project, List<string> violations)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }
    }

    /// <summary>All argument problems at once as INVALID_ARGUMENT, in the shape of the Host's own (the MCP server standard).</summary>
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

    /// <summary>
    /// The project a plan was approved for must still be the one the request resolves to (security review 2026-11-17).
    /// Requests that carry the caller's project name are resolved again at execution, and another open project could have
    /// taken that name since the approval; their fingerprints cover the request as written, so the Host sends the plan's
    /// project path beside the request instead of changing it. Without a planned path nothing is checked.
    /// </summary>
    public static void RequirePlanned(string? plannedPath, string resolvedPath)
    {
        if (plannedPath is { Length: > 0 } && !ProjectResolver.SamePath(resolvedPath, plannedPath))
        {
            throw NotStarted(new BridgeErrorException(ErrorReason.PlanStale,
                "The project name now opens a different project than the one the plan was approved for; nothing was changed. Create a new plan."));
        }
    }

    /// <summary>The writable project of this name, which must be the one the plan was approved for when a planned path is given.</summary>
    public static OpenProject Writable(IProjectCatalog catalog, string project, string? plannedPath)
    {
        OpenProject open = Writable(catalog, project);
        RequirePlanned(plannedPath, open.Path);
        return open;
    }

    /// <summary>Marks an error found before the transaction: nothing was changed (ADR-0010).</summary>
    public static BridgeErrorException NotStarted(BridgeErrorException error)
    {
        error.Error.Execution = ExecutionState.NotStarted;
        return error;
    }
}
