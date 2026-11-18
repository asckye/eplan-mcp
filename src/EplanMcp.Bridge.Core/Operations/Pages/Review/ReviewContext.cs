using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// What the checks of one page review share: the page as measured, what the reader read back, the request, and the
/// issue list in the order the checks add to it (the order is part of the result: the Host pages it).
/// </summary>
internal sealed class ReviewContext
{
    public ReviewContext(PageScene scene, JObject actual, JObject request)
    {
        Scene = scene;
        Actual = actual;
        Request = request;
        Targets = request["expected"] as JObject ?? new();
        // A72: an empty graphical representation is not an electrical edge.
        Graphical = scene.Graphical.Where(s => s.Length >= 2 && s.Any(p => !Equal(p, s[0]))).ToArray();
        Placements = scene.Page.Placements.Where(p => p.Parent is null || p.Properties.ContainsKey("text")).ToArray();
    }

    public PageScene Scene { get; }
    public JObject Actual { get; }
    public JObject Request { get; }
    /// <summary>The caller's expected objects by kind; empty when none were given.</summary>
    public JObject Targets { get; }
    public JArray Issues { get; } = new();
    /// <summary>Wire routes with at least two different points.</summary>
    public double[][][] Graphical { get; }
    /// <summary>Top-level placements and texts.</summary>
    public DrawingPlacement[] Placements { get; }

    /// <summary>Pages whose lines are wires: not model views (3D projections) and not P&amp;ID pages (pipes).</summary>
    public bool Schematic => Scene.PageType is not ("ModelView" or "ProcessAndInstrumentationDiagram");

    public bool Enabled(string check) => Request["checks"] is not JArray checks || checks.Values<string>().Contains(check, StringComparer.Ordinal);

    public void Add(string code, string severity, double[] position, IEnumerable<DrawingLocator> objects, string message, string tool, JObject args)
    {
        DrawingLocator[] locators = objects.ToArray();
        if (position.Length != 2)
        { position = Scene.Page.Placements.FirstOrDefault(p => locators.Any(l => l.Oid == p.Locator.Oid))?.Location ?? Actual["connections"]?.FirstOrDefault(c => locators.Any(l => l.Oid == (long?)c["locator"]?["oid"]))?["position"]?.ToObject<double[]>() ?? []; }
        if (tool is "eplan_placement_update" or "eplan_connection_create")
        { args["project"] = Scene.Project.Name; args["page"] = Scene.Page.Page; }
        if (tool == "eplan_placement_update" && args["locator"] is { } locator)
        { args = new JObject { ["project"] = Scene.Project.Name, ["page"] = Scene.Page.Page, ["moves"] = new JArray(args["x"] is { } x ? new JObject { ["locator"] = locator.DeepClone(), ["x_mm"] = x.DeepClone(), ["y_mm"] = args["y"]!.DeepClone() } : new JObject { ["locator"] = locator.DeepClone() }) }; }
        Issues.Add(new JObject { ["code"] = code, ["severity"] = severity, ["page"] = Scene.Page.Page, ["position"] = new JArray(position), ["objects"] = BridgeJson.FromObject(locators), ["message"] = message, ["fix"] = new JObject { ["tool"] = tool, ["args"] = args } });
    }

    public DrawingPlacement? Owner(string pin) => Scene.Page.Placements.FirstOrDefault(p => pin.StartsWith(FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}/"), StringComparison.Ordinal));

    public static bool Equal(double[] a, double[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) < 1e-8).All(v => v);

    public static bool Known(double[] point) => point.Length == 2 && point.All(v => !double.IsNaN(v) && !double.IsInfinity(v));

    public bool Near(double[] a, double[] b) => a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) <= ((double?)Request["tolerance_mm"] ?? 0.5)).All(v => v);

    public bool Connected(double[] location) => Graphical.Any(s => s.Any(p => Equal(p, location)));

    /// <summary>An expected or read connection end as "[device, pin]", the device resolved to the tag read from the page.</summary>
    public string End(JToken? e)
    {
        JObject? endpoint = e as JObject;
        string? name = (string?)endpoint?["device"];
        string? resolved = Actual["devices"]?.Select(d => (string?)d["tag"]).FirstOrDefault(n => n is not null && name is not null && DrawingNames.SameName(name, n, Scene.Page.Page));
        return new JArray(resolved ?? name ?? "", (string?)endpoint?["pin"] ?? "").ToString(Newtonsoft.Json.Formatting.None);
    }

    public bool SameEnds(JToken a, JToken b) => new HashSet<string>(new[] { End(a["from"]), End(a["to"]) }, StringComparer.Ordinal).SetEquals(new[] { End(b["from"]), End(b["to"]) });
}
