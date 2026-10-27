using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Reference lifecycle under the dispatcher's lock; A18/A31/A34/A46, without transactions or undo.</summary>
internal sealed class EplanReferenceProjects(IBridgeLog log) : IReferenceProjects
{
    public void Copy(string source, string destination)
    {
        MainThread.Assert();
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        if (source.EndsWith(".zw1", StringComparison.OrdinalIgnoreCase))
        { new EplanProjectFactory(log).Restore(source, destination); }
        else
        {
            using var manager = new ProjectManager();
            manager.CopyProject(source, destination, ProjectManager.CopyMode.Snapshot);
        }
    }
    public uint OpenReadOnly(string path)
    {
        MainThread.Assert();
        path = Path.GetFullPath(path);
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        using var manager = new ProjectManager();
        bool previous = manager.LockProjectByDefault;
        manager.LockProjectByDefault = false;
        try
        {
            Project project = manager.OpenProjects.FirstOrDefault(p => string.Equals(p.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase))
                ?? manager.OpenProject(path, ProjectManager.OpenMode.ReadOnly);
            if (!project.IsReadOnly)
            { throw new BridgeErrorException(ErrorReason.InvalidArgument, "The reference copy is already open for writing; it was not adopted."); }
            return project.DatabaseIdentifier;
        }
        finally { manager.LockProjectByDefault = previous; }
    }
    public void Close(string path, uint database)
    {
        MainThread.Assert();
        path = Path.GetFullPath(path);
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        EplanProjects.Read(open =>
        {
            Project? project = open.FirstOrDefault(p => string.Equals(p.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase));
            if (project is not null && (project.DatabaseIdentifier != database || !project.IsReadOnly))
            { throw new BridgeErrorException(ErrorReason.PlanStale, "The reference copy was reopened by another owner; it was not closed."); }
            project?.Close();
            return true;
        });
    }
}
