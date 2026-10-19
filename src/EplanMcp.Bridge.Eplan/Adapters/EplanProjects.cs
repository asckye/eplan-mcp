using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The open projects, for adapters. Main thread, inside the dispatcher's LockingStep. Adapters never use the
/// "current" project to find their target: an operation resolves the tool's argument with ProjectResolver and comes
/// here with the path (api-notes A11).
/// </summary>
internal static class EplanProjects
{
    /// <summary>
    /// Calls <paramref name="read"/> with every open project. <see cref="ProjectManager.OpenProjects"/> tries to lock
    /// each project it returns while <see cref="ProjectManager.LockProjectByDefault"/> is true (the default) and throws
    /// <c>ProjectLockingException</c> if one is locked elsewhere (EPLAN XML docs). A read must not depend on locks,
    /// so the flag is off for the call and restored afterwards, in case it is shared.
    /// </summary>
    public static T Read<T>(Func<Project[], T> read)
    {
        MainThread.Assert();
        using var manager = new ProjectManager();
        bool lockByDefault = manager.LockProjectByDefault;
        manager.LockProjectByDefault = false;
        try
        {
            return read(manager.OpenProjects);
        }
        finally
        {
            manager.LockProjectByDefault = lockByDefault;
        }
    }

    /// <summary>
    /// The open project whose <see cref="Project.ProjectFullName"/> is <paramref name="path"/>, as ProjectResolver
    /// returned it. Resolution and this call run in the same main-thread call, so a miss means a defect or a race
    /// with the engineer closing the project; it is reported as PROJECT_NOT_OPEN either way.
    /// </summary>
    public static Project ByPath(string path) =>
        Read(projects => projects.FirstOrDefault(p => string.Equals(p.ProjectFullName, path, StringComparison.OrdinalIgnoreCase)))
        ?? throw new BridgeErrorException(
            ErrorReason.ProjectNotOpen,
            "The project is no longer open in EPLAN. Call eplan_project_list to see the open projects.");
}
