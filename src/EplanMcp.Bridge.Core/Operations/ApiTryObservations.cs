using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Array and lifecycle locators bound observations to affected pages; project anchors do not widen them.</summary>
internal static class ApiTryObservations
{
    internal static string[]? Pages(ApiTryArgs args)
    {
        JObject parameters = ApiTryPlanOperation.Parameters(args.Parameters);
        string[]? shape = ApiTryCatalog.Shape(args.Group, parameters.Properties().Select(p => p.Name));
        if (shape is null)
        { return null; }
        var pages = new HashSet<string>(StringComparer.Ordinal);
        bool objects = ApiTryLifecycle.Effect(args.Group) is not null;
        foreach (string name in shape.Skip(2))
        {
            string[]? parameter = ApiTryCatalog.ParameterShape(shape[1], name);
            if (parameter is null || parameter[0] != "array" || ApiTryCatalog.Kinds(parameter[1]).Length == 0 && (!ApiTryCatalog.HandleType(parameter[1]) || parameter[1] == "Eplan.EplApi.Base.PointD"))
            { continue; }
            foreach (JToken locator in parameters[name]!)
            {
                objects = true;
                string? page = (string?)locator["page"];
                if (page is null || page.Length == 0)
                { return null; } // Unplaced arguments still need the project-wide observation.
                pages.Add(page);
            }
        }
        if (!objects || parameters["target"] is JObject target && target.ContainsKey("handle"))
        { return null; }
        foreach (JObject locator in parameters.Descendants().OfType<JObject>().Where(p => p.ContainsKey("digest")))
        {
            string page = (string)locator["page"]!;
            if (page.Length > 0)
            { pages.Add(page); }
            else if (!ReferenceEquals(locator, parameters["target"]) || !(ApiTryCatalog.Service(args.Group) || args.Group.EndsWith(".#ctor+Create", StringComparison.Ordinal)))
            { return null; }
        }
        if (ApiTryLifecycle.Effect(args.Group) is not null && parameters.Descendants().OfType<JObject>().Any(p => p.ContainsKey("handle")))
        { return null; }
        return pages.Count == 0 ? null : pages.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    internal static Dictionary<string, string> Capture(ISnapshotSource source, string[]? pages, Action check, Func<string, string?> drawing)
    {
        var state = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<long, string> page in source.Pages.Where(p => pages is null || pages.Contains(p.Value, StringComparer.Ordinal)).OrderBy(p => p.Key))
        {
            check();
            IReadOnlyList<SnapshotSourceObject> objects = source.ReadPage(page.Key, SnapshotProfiles.StandardName);
            string key = page.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
            state["snapshot:" + key] = SnapshotCanonical.Page(SnapshotProfiles.StandardName, objects[0], objects.Skip(1), SnapshotCanonical.Registered(SnapshotProfiles.StandardName)!).Hash;
            if (pages is not null)
            {
                string? geometry = drawing(page.Value);
                state[geometry is null ? "observation:drawing-unavailable:" + key : "drawing:" + key] = geometry is null ? "unavailable" : SnapshotCanonical.Sha256(geometry);
            }
            check();
        }
        foreach (string missing in (pages ?? []).Except(source.Pages.Select(p => p.Value), StringComparer.Ordinal))
        { state["observation:missing-page:" + missing] = "unavailable"; }
        return state;
    }
}
