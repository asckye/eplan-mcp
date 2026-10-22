using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectCompatibility"/> (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.7): what EPLAN
/// says of a project file without opening it (api-notes A31), so the Host learns a reference project's version before
/// anything opens, and so upgrades, it. Reads only. A missing file is FILE_NOT_FOUND before EPLAN throws S063073.
/// </summary>
public sealed class ProjectCompatibilityOperation(IProjectCatalog catalog, IProjectFiles files) : Operation<ProjectCompatibilityArgs, ProjectCompatibilityData>
{
    public override string Name => OperationNames.ProjectCompatibility;

    protected override ProjectCompatibilityData Execute(ProjectCompatibilityArgs args)
    {
        string path = args.Path?.Trim() ?? string.Empty;
        if (!ProjectPaths.IsPlainFullPath(path) || !path.EndsWith(".elk", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidArgument("'path' must be the full local path of a project's .elk file.");
        }

        if (!File.Exists(path))
        {
            throw new BridgeErrorException(
                ErrorReason.FileNotFound,
                "No project file is at this path.",
                retryable: false,
                new Dictionary<string, string> { ["file"] = Path.GetFileName(path) });
        }

        (string status, string version) = files.Compatibility(path);
        return new ProjectCompatibilityData
        {
            Status = status,
            Compatible = string.Equals(status, ClosedProject.Compatible, StringComparison.Ordinal),
            DatabaseVersion = version,
            Open = catalog.ListOpenProjects().Any(p => ProjectResolver.SamePath(p.Path, path)),
        };
    }
}
