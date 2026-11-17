using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Assigns a committed template in a fresh transaction, retaining only managed readback across commits.</summary>
internal static class EplanFunctionAssignment
{
    internal sealed class Result
    {
        public bool Assigned { get; set; }
        public string[]? Pins { get; set; }
        public int? Functions { get; set; }
        public string[]? UnplacedFunctions { get; set; }
        public string? Warning { get; set; }
    }

    internal static Function[] Functions(Project project, string tag)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new FunctionsFilter { Name = tag, ExactNameMatching = true };
        return [.. EplanNativePreconditions.Valid(finder.GetFunctions(filter)).OrderBy(f => f.ObjectIdentifier)];
    }

    internal static string[] Pins(Function function) => [.. function.Pins.Select(p => EplanPins.Name(p) ?? string.Empty)];

    internal static Result Assign(string projectPath, long sourceId, long targetId, string tag)
    {
        string[]? expected = null;
        var result = new Result();
        using (Transaction transaction = new TransactionManager().CreateTransaction())
        {
            Function[] functions;
            Function source, target;
            try
            {
                functions = Functions(EplanProjects.ByPath(projectPath), tag);
                source = functions.Single(f => f.ObjectIdentifier == sourceId);
                target = functions.Single(f => f.ObjectIdentifier == targetId);
            }
            catch (Exception failure)
            {
                WriteExceptionDiagnostics.Report(failure);
                result.Warning = "The committed functions could not be read for assignment. Inspect eplan_device_get before repeating it.";
                return result;
            }
            try
            { expected = Pins(source); }
            catch (Exception failure)
            {
                WriteExceptionDiagnostics.Report(failure);
                result.Warning = "The template's pin names could not be read; assignment pin verification is incomplete. Inspect eplan_device_get.";
            }
            source.Assign(target);
            transaction.Commit();
            GC.KeepAlive(functions);
        }
        result.Assigned = true;
        try
        {
            Function[] functions = Functions(EplanProjects.ByPath(projectPath), tag);
            result.Functions = functions.Length;
            result.UnplacedFunctions = [.. functions.Where(f => !f.IsPlaced).Select(f => f.Name)];
            result.Pins = Pins(functions.Single(f => f.ObjectIdentifier == targetId));
            if (expected is not null && !expected.SequenceEqual(result.Pins, StringComparer.Ordinal) || functions.Any(f => f.ObjectIdentifier == sourceId && !f.IsPlaced))
            { result.Warning = "Readback deviation: assigned function pins differ from the template or the source remains unplaced. Inspect eplan_device_get before placing another function."; }
            GC.KeepAlive(functions);
        }
        catch (Exception failure)
        {
            WriteExceptionDiagnostics.Report(failure);
            result.Warning = "The function assignment was committed, but its pins or remaining unplaced functions could not be read. Inspect eplan_device_get before repeating it.";
        }
        return result;
    }
}
