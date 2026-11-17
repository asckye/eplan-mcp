using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

#pragma warning disable CA2000 // A39: EPLAN owns created placements, including rollback paths.

/// <summary>
/// Places 3D layout objects as Layout3DSpikeTests observed (api-notes A136). Main thread, inside the operation's
/// transaction; the caller reads the result back with <see cref="EplanLayoutReader"/>.
/// </summary>
internal sealed class EplanLayoutWriter : ILayoutWriter
{
    public long PlaceEnclosure(string projectPath, string space, string part, string variant)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        InstallationSpace? target = project.InstallationSpaces.FirstOrDefault(s => EplanLayoutReader.Designation(s) == space);
        if (target is null)
        {
            target = new InstallationSpace();
            // The string becomes the designation (36452); with a '+' it would be stored with it (A136).
            target.Create(project, space);
        }

        var cabinet = new Cabinet();
        cabinet.Create(target, part, variant);
        // Created this way the enclosure has no parent and is not placed until it hangs below the space (A136).
        cabinet.SetParent(target, false);
        return cabinet.ObjectIdentifier;
    }
}
