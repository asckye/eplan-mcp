using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Same unfiltered enumeration as EplanPageRemover.Functions (A18), with persistent IDs (A32/A34).</summary>
internal sealed class EplanPlanFunctionReader : IPlanFunctionReader
{
    public IReadOnlyList<PlanFunction> Read(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        // The unfiltered finder can omit additional representations (A12); placements supply those, the finder supplies
        // unplaced functions. These fields need no property lists; never dispose returned StorableObjects (A39).
        IEnumerable<Function> functions = finder.GetFunctions(null)
            .Concat(finder.GetPages(null).SelectMany(p => p.AllPlacements.OfType<Function>()));
        return [.. functions.Select(f => new PlanFunction
        {
            Type = SnapshotCanonical.TypeOf(f.ToStringIdentifier()),
            Oid = f.ObjectIdentifier,
            Name = f.Name ?? string.Empty,
            IsMain = f.IsMainFunction,
            Page = f.Page?.Name,
        }).GroupBy(f => (f.Type, f.Oid)).Select(g => g.First())];
    }
}
