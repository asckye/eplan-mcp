using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDevicePartAssigner
{
#pragma warning disable CA1822 // IDevicePartAssigner's instance member; Small tests link this partial without the native service adapter.
    public (int Count, string? Warning) AssignUnplacedFunctions(string projectPath, string tag)
    {
        MainThread.Assert();
        int count = 0;
        var warnings = new List<string?>();
        var assignedSources = new HashSet<long>();
        long[] targets;
        try
        {
            targets = EplanFunctionAssignment.Functions(EplanProjects.ByPath(projectPath), tag)
                .Where(f => f.IsPlaced && !f.IsMainFunction).Select(f => f.ObjectIdentifier).ToArray();
        }
        catch (Exception failure)
        {
            WriteExceptionDiagnostics.Report(failure);
            return (0, "Part was committed, but secondary functions could not be read for assignment. Inspect eplan_device_get.");
        }
        foreach (long targetId in targets)
        {
            long? sourceId;
            try
            {
                Function[] functions = EplanFunctionAssignment.Functions(EplanProjects.ByPath(projectPath), tag);
                Function target = functions.Single(f => f.ObjectIdentifier == targetId);
                string[] pins = EplanFunctionAssignment.Pins(target);
                sourceId = functions.FirstOrDefault(f => !f.IsPlaced && !assignedSources.Contains(f.ObjectIdentifier) && f.FunctionCategory == target.FunctionCategory
                    && EplanFunctionAssignment.Pins(f).SequenceEqual(pins, StringComparer.Ordinal))?.ObjectIdentifier;
            }
            catch (Exception failure)
            {
                WriteExceptionDiagnostics.Report(failure);
                warnings.Add($"Part was committed, but assignment for function {targetId} could not be checked. Inspect eplan_device_get.");
                continue;
            }
            if (sourceId is not { } source)
            { continue; }
            EplanFunctionAssignment.Result result = EplanFunctionAssignment.Assign(projectPath, source, targetId, tag);
            assignedSources.Add(source);
            if (result.Assigned)
            { count++; }
            warnings.Add(result.Warning);
        }
        string warning = LayoutGuard.Summary(warnings);
        return (count, warning.Length == 0 ? null : warning);
    }
#pragma warning restore CA1822
}
