using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Removes rails, ducts and device placements by object ID; main thread, inside the operation's transaction.</summary>
internal sealed class EplanLayoutRemover : ILayoutRemover
{
    public void Remove(string projectPath, IReadOnlyList<long> ids)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Function3D[] all = [.. EplanNativePreconditions.Valid(finder.GetFunctions3D(null))];
        foreach (long id in ids)
        {
            // Looked up again per ID: removing a device can change its rail's object list (A39: no reuse after a write).
            Function3D target = all.FirstOrDefault(f => f.IsValid && f.ObjectIdentifier == id)
                ?? throw new InvalidOperationException("A planned layout object is no longer in the project.");
            target.Remove();
        }
    }
}
