using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed class EplanUnplacedFunctionRemover : IUnplacedFunctionRemover
{
    public IReadOnlyList<NamedFunctionRow> Find(string project, IReadOnlyList<string> tags)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        return Functions(finder, tags).Select(function =>
        {
            bool? placed = PlacementStatus(function);
            return new NamedFunctionRow
            {
                Name = function.Name,
                Type = SnapshotCanonical.TypeOf(function.ToStringIdentifier()),
                Oid = function.ObjectIdentifier,
                Category = function.FunctionCategory.ToString(),
                Main = function.IsMainFunction,
                Placed = placed != false,
                Page = placed == true ? function.Page?.Name : null
            };
        }).ToArray();
    }

    public Action Prepare(string project, IReadOnlyList<NamedFunctionRow> rows)
    {
        MainThread.Assert();
        return () =>
        {
            MainThread.Assert();
            try
            {
                using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
                string[] names = rows.Select(row => row.Name).Distinct(StringComparer.Ordinal).ToArray();
                var functions = Functions(finder, names).ToDictionary(Key);
                foreach (NamedFunctionRow row in rows)
                {
                    OperationContext.Current.ThrowIfCancellationRequested();
                    if (!functions.TryGetValue((row.Type, row.Oid), out Function? function) || !function.IsValid
                        || function.Name != row.Name || PlacementStatus(function) != false)
                    {
                        throw new BridgeErrorException(ErrorReason.PlanStale, $"'{row.Name}' ({row.Type}/{row.Oid}) disappeared, changed name or is placed.");
                    }
                    function.Remove();
                }
            }
            catch (Exception ex)
            {
                WriteExceptionDiagnostics.Report(ex);
                throw;
            }
        };
    }

    private static IEnumerable<Function> Functions(DMObjectsFinder finder, IReadOnlyList<string> tags)
    {
        var seen = new HashSet<(int, long)>();
        foreach (Function function in EplanPropertyReadAdapter.Functions(finder, OperationContext.Current.ThrowIfCancellationRequested))
        {
            string name = function.Name ?? throw new InvalidOperationException("Cannot verify a function with an unreadable name.");
            if (tags.Any(tag => name == tag || name.StartsWith(tag + ":", StringComparison.Ordinal)) && seen.Add(Key(function)))
            {
                yield return function;
            }
        }
    }

    private static (int, long) Key(Function function) =>
        (SnapshotCanonical.TypeOf(function.ToStringIdentifier()), function.ObjectIdentifier);

    private static bool? PlacementStatus(Function function)
    {
        try
        {
            return function.IsPlaced;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or System.OperationCanceledException or global::Eplan.EplApi.DataModel.OperationCanceledException))
        {
            // An unreadable placement status counts as placed: never delete the function on missing evidence.
            WriteExceptionDiagnostics.Report(ex);
            return null;
        }
    }
}
