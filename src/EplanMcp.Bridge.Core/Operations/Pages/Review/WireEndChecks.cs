using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>off_grid for wires: a bend that is no symbol pin and lies off the page grid.</summary>
internal static class WireBendGridCheck
{
    public static void Run(ReviewContext c)
    {
        PageScene scene = c.Scene;
        foreach (double[] point in c.Graphical.SelectMany(s => s).Where(ReviewContext.Known).GroupBy(p => string.Join("/", p)).Select(g => g.First()))
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (c.Enabled("off_grid") && scene.Page.GridSize > 0 && !scene.Pins.Values.Any(p => ReviewContext.Equal(p.Location, point)) && point.Any(v => Math.Abs(v / scene.Page.GridSize - Math.Round(v / scene.Page.GridSize)) > 1e-8))
            { c.Add("off_grid", "info", point, [], "Wire bend is off the page grid.", "eplan_connection_create", new() { ["page"] = scene.Page.Page }); }
        }
    }
}

/// <summary>dangling_ends: a wire end on no symbol pin or node. An error on schematic pages, a warning elsewhere (A147).</summary>
internal static class DanglingEndsCheck
{
    public static void Run(ReviewContext c)
    {
        // A73: bends are not ends, and the exact page origin is an unknown/default coordinate.
        foreach (double[] end in c.Graphical.SelectMany(s => new[] { s[0], s[s.Length - 1] }).Where(ReviewContext.Known).Where(p => p[0] != 0 || p[1] != 0).GroupBy(p => string.Join("/", p)).Select(g => g.First()))
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (c.Enabled("dangling_ends") && !c.Scene.Pins.Values.Any(p => ReviewContext.Equal(p.Location, end)))
            { c.Add("dangling_end", c.Schematic ? "error" : "warning", end, [], "Wire end has no symbol pin or node.", "eplan_connection_create", new() { ["page"] = c.Scene.Page.Page }); }
        }
    }
}

/// <summary>unconnected_pins: a named symbol's pin without a wire; an error when the expected connections name that pin.</summary>
internal static class UnconnectedPinsCheck
{
    public static void Run(ReviewContext c)
    {
        PageScene scene = c.Scene;
        foreach (var pin in scene.Pins)
        {
            DrawingPlacement? owner = c.Owner(pin.Key);
            if (c.Enabled("unconnected_pins") && owner?.Properties.ContainsKey("name") == true && !c.Connected(pin.Value.Location))
            {
                // A83: an interruption point is one wire end even with two connection points (BP); one connected pin covers it, an
                // open one is reported once, at its tail (the highest pin index, as the planner attaches wires).
                string prefix = FormattableString.Invariant($"{owner.Locator.Type}/{owner.Locator.Oid}/");
                if (owner.Kind == "InterruptionPoint" && scene.Pins.Any(other => other.Key.StartsWith(prefix, StringComparison.Ordinal)
                    && (c.Connected(other.Value.Location) || PinIndex(other.Key) > PinIndex(pin.Key))))
                { continue; }
                string? designation = (string?)c.Actual["devices"]?.FirstOrDefault(d => (long?)d["locator"]?["oid"] == owner.Locator.Oid)?["pins"]?[pin.Key.Split('/').Last()];
                bool required = c.Targets["connections"]?.Any(t => new[] { t["from"], t["to"] }.Any(e => e is JObject endpoint && (string?)endpoint["device"] is { } name && DrawingNames.SameName(name, owner.Properties["name"], scene.Page.Page) && (string?)endpoint["pin"] == designation)) == true;
                c.Add("unconnected_pin", required ? "error" : "warning", pin.Value.Location, [owner.Locator], "Symbol pin has no graphical connection.", "eplan_connection_create", new() { ["page"] = scene.Page.Page, ["pin_index"] = pin.Key.Split('/').Last() });
            }
        }
    }

    private static int PinIndex(string key) => int.TryParse(key.Split('/').Last(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ? index : -1;
}
