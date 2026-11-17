using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// A project file's database version and compatibility without opening it, as spike S27 read them in the dispatcher
/// (api-notes A31: 0.02–0.3 s, the project stays closed). Main thread, inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanProjectFiles : IProjectFiles
{
    public (string Status, string DatabaseVersion) Compatibility(string linkFile)
    {
        MainThread.Assert();
        using var manager = new ProjectManager();
        return (manager.GetProjectDataCompatibility(linkFile).ToString(), manager.GetProjectDatabaseVersion(linkFile).ToString());
    }
}
