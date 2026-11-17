using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads the open projects. Runs on the main thread inside the dispatcher's LockingStep; the projects come from
/// <see cref="EplanProjects.Read"/>, which keeps EPLAN from locking them. "Current" is only reported here, never used
/// to pick a target (api-notes A11).
/// </summary>
internal sealed class EplanProjectCatalog : IProjectCatalog
{
    public IReadOnlyList<OpenProject> ListOpenProjects()
    {
        MainThread.Assert();
        string? currentPath = new SelectionSet().GetCurrentProject(false)?.ProjectFullName;

        return EplanProjects.Read<IReadOnlyList<OpenProject>>(projects =>
        [
            .. projects.Select(project => new OpenProject
            {
                Name = project.ProjectName,
                Path = project.ProjectFullName,
                Current = currentPath is not null && string.Equals(project.ProjectFullName, currentPath, StringComparison.OrdinalIgnoreCase),
                ReadOnly = project.IsReadOnly,
            }),
        ]);
    }
}
