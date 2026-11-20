using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>
/// Read-only evidence for api-note A170: which reads of "all functions" return which objects. Compares the unfiltered
/// read, the read per page (FunctionsFilter.Page), each page's AllPlacements, and the unfiltered reads of terminals,
/// terminal strips and PLCs. Identity is the runtime type and the object identifier. Nothing is written.
/// </summary>
public sealed class FunctionReadsProbeAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeFunctionReadsProbe"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string file = "";
        oActionCallingContext.GetParameter("REQUEST", ref file);
        var result = new JObject { ["success"] = false };
        try
        {
            using var locking = new LockingStep();
            JObject input = JObject.Parse(File.ReadAllText(file));
            string wanted = (string)input["project"]!;
            using var manager = new ProjectManager();
            Project project = manager.OpenProjects.Single(p => string.Equals(p.ProjectFullName, wanted, StringComparison.OrdinalIgnoreCase));
            using var finder = new DMObjectsFinder(project);
            Page[] pages = finder.GetPages(null);

            var unfiltered = Index(finder.GetFunctions(null));
            var perPage = new Dictionary<(Type, long), Function>();
            var placements = new Dictionary<(Type, long), Function>();
            foreach (Page page in pages)
            {
                using var filter = new FunctionsFilter { Page = page };
                foreach (var pair in Index(finder.GetFunctions(filter)))
                { perPage[pair.Key] = pair.Value; }
                foreach (var pair in Index(page.AllPlacements.OfType<Function>()))
                { placements[pair.Key] = pair.Value; }
            }
            var complete = new Dictionary<(Type, long), Function>(unfiltered);
            foreach (var pair in perPage)
            { complete[pair.Key] = pair.Value; }

            Function[] omitted = [.. perPage.Where(p => !unfiltered.ContainsKey(p.Key)).Select(p => p.Value)];
            Function[] notCompensated = [.. omitted.Where(f => !placements.ContainsKey((f.GetType(), f.ObjectIdentifier)))];
            Function[] placementsOnly = [.. placements.Where(p => !complete.ContainsKey(p.Key)).Select(p => p.Value)];
            result["pages"] = pages.Length;
            result["unfiltered"] = unfiltered.Count;
            result["perPage"] = perPage.Count;
            result["allPlacementsFunctions"] = placements.Count;
            result["complete"] = complete.Count;
            result["omittedByUnfiltered"] = Summary(omitted);
            result["omittedAndNotInAllPlacements"] = Summary(notCompensated);
            result["onlyInAllPlacements"] = Summary(placementsOnly);

            // A filter by name alone misses the omitted functions (A170); does a category beside the name find them?
            int byNameOnly = 0, byNameAndCategory = 0, asked = 0;
            foreach (Function function in omitted.Where(f => !string.IsNullOrEmpty(f.Name)).Take(40))
            {
                asked++;
                using var name = new FunctionsFilter { Name = function.Name, ExactNameMatching = true };
                if (finder.GetFunctions(name).Any(f => f.ObjectIdentifier == function.ObjectIdentifier))
                { byNameOnly++; }
                using var both = new FunctionsFilter { Name = function.Name, ExactNameMatching = true, FunctionCategory = function.FunctionCategory };
                if (finder.GetFunctions(both).Any(f => f.ObjectIdentifier == function.ObjectIdentifier))
                { byNameAndCategory++; }
            }
            result["nameFilter"] = new JObject { ["asked"] = asked, ["foundByNameOnly"] = byNameOnly, ["foundByNameAndCategory"] = byNameAndCategory };

            result["terminals"] = Typed(finder.GetTerminals(null), complete.Values.OfType<Terminal>());
            result["terminalStrips"] = Typed(finder.GetTerminalStrips(null), complete.Values.OfType<TerminalStrip>());
            result["plcs"] = Typed(finder.GetPLCs(null), complete.Values.OfType<PLC>());
            result["success"] = true;
        }
        catch (Exception error) { result["error"] = error.ToString(); }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }

    private static Dictionary<(Type, long), Function> Index(IEnumerable<Function> functions)
    {
        var index = new Dictionary<(Type, long), Function>();
        foreach (Function function in functions.Where(f => f.IsValid))
        { index[(function.GetType(), function.ObjectIdentifier)] = function; }
        return index;
    }

    private static JObject Summary(Function[] functions) => new()
    {
        ["count"] = functions.Length,
        ["placed"] = functions.Count(f => f.Page is not null),
        ["main"] = functions.Count(f => f.IsMainFunction),
        ["byCategory"] = new JObject(functions.GroupBy(f => f.FunctionCategory.ToString()).OrderByDescending(g => g.Count()).Take(8).Select(g => new JProperty(g.Key, g.Count()))),
        ["sample"] = new JArray(functions.Take(5).Select(f => f.Name + " @ " + (f.Page?.Name ?? "(unplaced)"))),
    };

    /// <summary>An unfiltered typed read beside the same type taken from the complete function read.</summary>
    private static JObject Typed<T>(IEnumerable<T> unfilteredRead, IEnumerable<T> fromComplete) where T : Function
    {
        var unfiltered = new HashSet<long>(unfilteredRead.Where(f => f.IsValid).Select(f => f.ObjectIdentifier));
        T[] complete = [.. fromComplete];
        T[] omitted = [.. complete.Where(f => !unfiltered.Contains(f.ObjectIdentifier))];
        return new JObject
        {
            ["unfiltered"] = unfiltered.Count,
            ["inCompleteRead"] = complete.Length,
            ["omittedByUnfiltered"] = omitted.Length,
            ["onlyInUnfiltered"] = unfiltered.Count(id => complete.All(f => f.ObjectIdentifier != id)),
            ["sample"] = new JArray(omitted.Take(5).Select(f => f.Name + " @ " + (f.Page?.Name ?? "(unplaced)"))),
        };
    }
}
