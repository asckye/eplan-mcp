using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Records native PLC representations and rendered references without defining product behavior.</summary>
public sealed class PlcOverviewSpikeAction : IEplAction
{
    private static readonly int[] _propertyNumbers = [20010, 20300, 20301, 20472, 20473];

    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargePlcOverviewSpike"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string file = "";
        oActionCallingContext.GetParameter("REQUEST", ref file);
        var result = new JObject { ["success"] = false };
        try
        {
            using var locking = new LockingStep();
            MainThread.Use(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            JObject input = JObject.Parse(File.ReadAllText(file));
            Project project = new SelectionSet().GetCurrentProject(false);
            string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
            string circuitName = (string?)input["circuitPage"] ?? "";
            string overviewName = (string?)input["overviewPage"] ?? "";
            if (!string.Equals(project.ProjectFullName, Path.Combine(home, "scratch", "writes", "LargeWrite"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(project.ProjectFullName, (string?)input["project"], StringComparison.OrdinalIgnoreCase)
                || !circuitName.StartsWith("=GAA+A1&EFS1/", StringComparison.Ordinal)
                || !overviewName.StartsWith("=GAA+A1&EFS1/", StringComparison.Ordinal)
                || string.Equals(circuitName, overviewName, StringComparison.Ordinal))
            { throw new InvalidOperationException("Only explicit new Circuit/Overview pages of current scratch LargeWrite are permitted."); }
            Page circuit = project.Pages.Single(p => p.Name == circuitName && p.PageType.ToString() == "Circuit");
            Page overview = project.Pages.Single(p => p.Name == overviewName && p.PageType.ToString() == "Overview");
            string mode = (string?)input["mode"] ?? "";
            if (mode is not ("read" or "place"))
            { throw new InvalidOperationException("Mode must be read or place."); }
            DocumentTypeManager.DocumentType placementType = (string?)input["placementType"] switch
            {
                "Overview" => DocumentTypeManager.DocumentType.Overview,
                "PLCCardOverview" => DocumentTypeManager.DocumentType.PLCCardOverview,
                "PairCrossReference" => DocumentTypeManager.DocumentType.PairCrossReference,
                _ => throw new InvalidOperationException("Unsupported PLC spike placement type."),
            };
            JObject[] items = input["items"]!.Children<JObject>().ToArray();
            result["mode"] = mode;
            result["placementType"] = input["placementType"]!.DeepClone();
            result["circuitPage"] = circuitName;
            result["overviewPage"] = overviewName;
            if (mode == "place")
            {
                var placements = new JArray();
                result["placements"] = placements;
                try
                { Place(project, circuit, overview, placementType, items, placements); }
                catch (Exception error) { result["placementError"] = Error(error); }
            }
            // A55: reacquire functions after the transaction; never inspect retained new native wrappers.
            result["observation"] = Read(project, items);
            result["success"] = true;
        }
        catch (Exception error) { result["error"] = error.ToString(); }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }

    private static void Place(Project project, Page circuit, Page overview, DocumentTypeManager.DocumentType placementType,
        JObject[] items, JArray results)
    {
        // A52: discover library variants before entering SafetyPoint, retaining resolution failures per item.
        var prepared = new List<(JObject Item, SymbolVariant? Variant, Exception? Error)>();
        foreach (JObject item in items)
        {
            try
            {
                SymbolVariant variant = project.SymbolLibraries.Single(l => l.Name == "SPECIAL").Symbols
                    .Single(s => s.Name == (string)item["symbol"]!).Variants.Single(v => v.VariantNr == (int)item["variant"]!);
                prepared.Add((item, variant, null));
            }
            catch (Exception error) { prepared.Add((item, null, error)); }
        }
        using var scope = new TransactionScope("PLC overview D8 spike");
        try
        {
            foreach ((JObject item, SymbolVariant? variant, Exception? resolutionError) in prepared)
            {
                var row = new JObject { ["item"] = item.DeepClone(), ["success"] = false };
                results.Add(row);
                if (resolutionError is not null)
                {
                    row["error"] = Error(resolutionError);
                    continue;
                }
                try
                {
                    Function function = FindFunctions(project, (string)item["name"]!)
                        .First(f => f.Page?.Name == circuit.Name);
                    function.PlaceAt(overview, new PointD((double)item["x"]!, (double)item["y"]!), placementType, variant!);
                    row["success"] = true;
                }
                catch (Exception error) { row["error"] = Error(error); }
            }
            scope.Commit();
            scope.CloseUndo();
        }
        catch { scope.Rollback(); throw; }
    }

    private static Function[] FindFunctions(Project project, string name)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new FunctionsFilter { Name = name };
        // A11: the native name filter matches prefixes, so exact matching is a separate step.
        return finder.GetFunctions(filter).Where(f => string.Equals(f.Name, name, StringComparison.Ordinal))
            .OrderBy(f => f.ObjectIdentifier).ToArray();
    }

    private static JArray Read(Project project, JObject[] items) => new(items.Select(item => new JObject
    {
        ["name"] = item["name"]!.DeepClone(),
        ["functions"] = Observe(() => new JArray(FindFunctions(project, (string)item["name"]!).Select(ReadFunction))),
    }));

    private static JObject ReadFunction(Function function) => new()
    {
        ["oid"] = Observe(() => function.ObjectIdentifier),
        ["name"] = Observe(() => function.Name),
        ["page"] = Observe(() => function.Page?.Name),
        ["isMainFunction"] = Observe(() => function.IsMainFunction),
        ["location"] = Observe(() => new JArray(function.Location.X, function.Location.Y)),
        ["symbol"] = new JObject
        {
            ["library"] = Observe(() => function.SymbolVariant?.SymbolLibraryName),
            ["name"] = Observe(() => function.SymbolVariant?.SymbolName),
            ["variant"] = Observe(() => function.SymbolVariant?.VariantNr),
        },
        ["properties"] = Observe(() =>
        {
            using FunctionPropertyList properties = function.Properties;
            var values = new JObject();
            foreach (int number in _propertyNumbers)
            {
                values[number.ToString(CultureInfo.InvariantCulture)] = Observe(() =>
                {
                    using AnyPropertyId id = number;
                    using PropertyValue value = properties[id];
                    return value.IsEmpty ? null : value.ToString();
                });
            }
            return values;
        }),
        ["propertyPlacements"] = Observe(() => ReadPropertyPlacements(function)),
    };

    private static JArray ReadPropertyPlacements(Function function)
    {
        Page? page = function.Page;
        if (page is null)
        { return new JArray(); }
        // A57: context placements are absent from AllPlacements and share their parent's object identifier.
        IEnumerable<(PropertyPlacement Placement, string Source)> placements = function.PropertyPlacements.Select(p => (p, "function"))
            .Concat(page.AllPlacements.OfType<PropertyPlacement>().Select(p => (p, "page")));
        var rows = new JArray();
        foreach ((PropertyPlacement placement, string source) in placements)
        {
            try
            {
                if (placement.Parent?.ObjectIdentifier != function.ObjectIdentifier || placement.Page?.Name != page.Name)
                { continue; }
                rows.Add(new JObject
                {
                    ["source"] = source,
                    ["propertyId"] = Observe(() => { using AnyPropertyId id = placement.DisplayedProperty; return id.AsInt; }),
                    ["index"] = Observe(() => { using AnyPropertyId id = placement.DisplayedProperty; return id.Index; }),
                    ["text"] = Observe(() => placement.GetDisplayString()),
                });
            }
            catch (Exception error) { rows.Add(Error(error)); }
        }
        return rows;
    }

    private static JObject Error(Exception error) => new() { ["type"] = error.GetType().Name, ["message"] = error.Message };

    private static JToken Observe(Func<object?> read)
    {
        try
        {
            object? value = read();
            return value is null ? JValue.CreateNull() : value is JToken token ? token : JToken.FromObject(value);
        }
        catch (Exception error)
        { return new JObject { ["readError"] = error.Message, ["type"] = error.GetType().Name }; }
    }
}
