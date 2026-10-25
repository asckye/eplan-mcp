using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Same unfiltered enumeration as EplanPageRemover.Functions (A18), with persistent IDs (A32/A34).</summary>
internal sealed class EplanPlanFunctionReader : IPlanNameReader
{
    public IReadOnlyList<PlanFunction> Read(string projectPath) =>
        Enumerate(projectPath, OperationContext.Current.ThrowIfCancellationRequested, identity: false).Functions;

    public PlanNameInventory ReadNames(string projectPath, Action checkpoint) => Enumerate(projectPath, checkpoint, identity: true);

    private static PlanNameInventory Enumerate(string projectPath, Action checkpoint, bool identity)
    {
        MainThread.Assert();
        checkpoint();
        Project project = EplanProjects.ByPath(projectPath);
        var inventory = new PlanNameInventory();
        if (identity)
        {
            // A36: acquire the list once and dispose only the list/value, never the project (A39).
            using ProjectPropertyList properties = project.Properties;
            using PropertyValue guid = properties.PROJ_GUID;
            inventory.ProjectGuid = guid.IsEmpty ? null : guid.ToString();
        }

        using var finder = new DMObjectsFinder(project);
        // The unfiltered finder can omit additional representations (A12); placements supply those, the finder supplies
        // unplaced functions. These fields need no property lists; never dispose returned StorableObjects (A39).
        checkpoint();
        Add(finder.GetFunctions(null));
        checkpoint();
        Page[] pages = finder.GetPages(null);
        checkpoint();
        foreach (Page page in pages)
        {
            checkpoint();
            inventory.Pages.Add(page.Name ?? string.Empty);
            Add(page.AllPlacements);
        }

        checkpoint();
        return inventory;

        void Add(IEnumerable<StorableObject> objects)
        {
            checkpoint();
            int count = 0;
            foreach (StorableObject obj in objects)
            {
                if (count++ % 128 == 0)
                {
                    checkpoint();
                }

                if (obj is Function f)
                {
                    inventory.AddFunction(new PlanFunction
                    {
                        Type = SnapshotCanonical.TypeOf(f.ToStringIdentifier()),
                        Oid = f.ObjectIdentifier,
                        Name = f.Name ?? string.Empty,
                        IsMain = f.IsMainFunction,
                        Page = f.Page?.Name,
                    });
                }
            }

            checkpoint();
        }
    }
}
