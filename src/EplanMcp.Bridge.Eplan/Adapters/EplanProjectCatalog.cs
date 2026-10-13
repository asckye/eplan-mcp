using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads the open projects. Runs on the main thread inside the dispatcher's LockingStep.
/// <para>
/// <see cref="ProjectManager.OpenProjects"/> tries to lock every project it returns while
/// <see cref="ProjectManager.LockProjectByDefault"/> is true (the default) and throws
/// <c>ProjectLockingException</c> if one cannot be locked (EPLAN XML docs; design 4.4). A read-only listing must
/// not depend on locks, so the flag is switched off for this call and restored afterwards in case it is shared.
/// </para>
/// </summary>
internal sealed class EplanProjectCatalog : IProjectCatalog
{
    public IReadOnlyList<OpenProject> ListOpenProjects()
    {
        MainThread.Assert();
        string? currentPath = new SelectionSet().GetCurrentProject(false)?.ProjectFullName;

        using var manager = new ProjectManager();
        bool lockByDefault = manager.LockProjectByDefault;
        manager.LockProjectByDefault = false;
        try
        {
            return [.. manager.OpenProjects.Select(project => new OpenProject
            {
                Name = project.ProjectName,
                Path = project.ProjectFullName,
                Current = currentPath is not null && string.Equals(project.ProjectFullName, currentPath, StringComparison.OrdinalIgnoreCase),
                ReadOnly = project.IsReadOnly,
            })];
        }
        finally
        {
            manager.LockProjectByDefault = lockByDefault;
        }
    }
}
