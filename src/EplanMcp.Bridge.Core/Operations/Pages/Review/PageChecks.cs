using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// path_straddle (batch 2, owner 2026-11-16): EPLAN numbers a device's column from its insertion point; pins in another
/// column make the cross-reference point at a column the reader does not find them in.
/// </summary>
internal static class PathStraddleCheck
{
    public static void Run(ReviewContext c)
    {
        ConnectionCreatePlanner.Scene scene = c.Scene;
        if (!c.Enabled("path_straddle") || scene.Page.ColumnEdges is not { Length: > 2 } edges)
        { return; }
        int Column(double x) => Array.FindLastIndex(edges, e => e <= x + 1e-6);
        foreach (DrawingPlacement p in scene.Page.Placements.Where(p => p.Parent is null && p.Properties.ContainsKey("name") && !LayoutGuard.Container(p) && p.Location.Length == 2))
        {
            string owner = FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}/");
            int home = Column(p.Location[0]);
            int[] columns = [.. scene.Pins.Where(pin => pin.Key.StartsWith(owner, StringComparison.Ordinal) && pin.Value.Location.Length == 2).Select(pin => Column(pin.Value.Location[0])).Distinct()];
            if (home < 0 || home >= edges.Length - 1 || columns.All(column => column == home))
            { continue; }
            c.Add("path_straddle", "warning", p.Location, [p.Locator], FormattableString.Invariant($"Pins lie in columns {string.Join(", ", columns.Select(column => column + 1))}, the symbol is numbered in column {home + 1}; keep the symbol and its pins in one column."),
                "eplan_placement_update", new() { ["locator"] = BridgeJson.FromObject(p.Locator) });
        }
    }
}

/// <summary>page_setup (batch 2): a page whose frame or grid differs from most pages of its type was usually made from the wrong template.</summary>
internal static class PageSetupCheck
{
    public static void Run(ReviewContext c)
    {
        if (!c.Enabled("page_setup") || c.Actual["pageSetup"] is not JObject setup || setup["peers"] is not JArray { Count: >= 3 } peers)
        { return; }
        void Odd(string field, string argument, string label)
        {
            var groups = peers.GroupBy(p => p[field]?.ToString(Newtonsoft.Json.Formatting.None) ?? "null", StringComparer.Ordinal).OrderByDescending(g => g.Count()).ToArray();
            string mine = setup[field]?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
            if (groups[0].Key == mine || groups[0].Key == "null" || groups.Length > 1 && groups[1].Count() == groups[0].Count() || groups.FirstOrDefault(g => g.Key == mine)?.Count() * 2 >= peers.Count)
            { return; }
            JToken wanted = groups[0].First()[field]!;
            c.Add("page_setup", "warning", [], [], FormattableString.Invariant($"The {label} is {setup[field]}; {groups[0].Count()} of {peers.Count} {setup["type"]} pages use {wanted}."),
                "eplan_page_update", new() { ["project"] = c.Scene.Project.Name, ["pages"] = new JArray(new JObject { ["name"] = c.Scene.Page.Page, [argument] = wanted.DeepClone() }) });
        }
        Odd("frame", "plot_frame", "plot frame");
        Odd("grid", "grid_size_mm", "grid");
    }
}

/// <summary>
/// wire_number (batch 3, owner 2026-11-16): EPLAN's check reports missing potentials (P005001/P005010) and empty or
/// unnumbered device tags (P017001/P017002), read as eplan_message, but no message covers wires left without a number or
/// tags left as a placeholder (eplan.help 2026 check run messages, classes 005 and 017).
/// </summary>
internal static class WireNumberCheck
{
    public static void Run(ReviewContext c)
    {
        // Live 2026-11-16 (official =EC1+A3&EFS1/1): the cores of the cables on this page carry their core marking as the number;
        // they are left out. Wire.Cable is no cable test: every wire with a definition point reads "<definition-points>".
        var cores = new HashSet<long>(c.Actual["cables"]?.SelectMany(cable => cable["cores"] ?? new JArray()).Select(core => (long?)core["locator"]?["oid"] ?? 0) ?? []);
        if (!c.Enabled("wire_number") || c.Actual["connections"] is not JArray all || all.Where(w => !cores.Contains((long?)w["locator"]?["oid"] ?? 0)).ToArray() is not { Length: > 0 } wires)
        { return; }
        JToken[] open = [.. wires.Where(w => string.IsNullOrEmpty((string?)w["wireNumber"]))];
        JObject numbering = new() { ["project"] = c.Scene.Project.Name, ["overwrite"] = "none" };
        if (open.Length == wires.Length)
        { c.Add("wire_number", "info", [], [], FormattableString.Invariant($"None of the {wires.Length} wires on this page has a wire number yet; number them with eplan_connection_number when the drawing is done."), "eplan_connection_number", numbering); }
        else if (open.Length > 0)
        {
            // Live 2026-11-16: the official =EC1+A3&EFS1/1 numbers some plain wires and not the ones inside boxes and plugs, as
            // the EPLAN numbering schemes do; a page with some numbers is normal, so the open wires are one note, not warnings.
            c.Add("wire_number", "info", [], [.. open.Take(20).Select(w => w["locator"] is JObject l ? l.ToObject<DrawingLocator>() : null).OfType<DrawingLocator>()],
                FormattableString.Invariant($"{open.Length} of {wires.Length} wires on this page have no wire number (up to 20 listed); check that the numbering scheme leaves them out on purpose."), "eplan_connection_number", numbering);
        }
    }
}

/// <summary>placeholder_tag (batch 3): a device tag that still contains "?".</summary>
internal static class PlaceholderTagCheck
{
    public static void Run(ReviewContext c)
    {
        if (!c.Enabled("placeholder_tag") || c.Actual["devices"] is not JArray devices)
        { return; }
        foreach (JToken device in devices.Where(d => (string?)d["tag"] is { } tag && tag.Contains('?')))
        {
            c.Add("placeholder_tag", "warning", device["position"]?.ToObject<double[]>() ?? [], device["locator"] is JObject l ? [l.ToObject<DrawingLocator>()!] : [],
                "Device tag " + (string?)device["tag"] + " is still a placeholder; give the device its real tag.", "eplan_device_update", new() { ["project"] = c.Scene.Project.Name, ["tag"] = device["tag"]!.DeepClone() });
        }
    }
}

/// <summary>
/// box_crossings. MX_Dryer16 2026-11-08: cable texts ran across the +M location box border; the official pages keep them
/// inside the box. The box's own texts (its +M label sits on the corner) do not count.
/// </summary>
internal static class BoxCrossingsCheck
{
    public static void Run(ReviewContext c)
    {
        if (!c.Enabled("box_crossings"))
        { return; }
        static bool Area(double[]? r) => r is { Length: 4 } && r[0] < r[2] && r[1] < r[3];
        DrawingPlacement[] boxes = [.. c.Scene.Page.Placements.Where(b => b.Kind == "LocationBox" && Area(b.Bounds))];
        foreach (DrawingPlacement text in c.Scene.Page.Placements.Where(t => t.Kind is "Text" or "PropertyPlacement" && Area(t.Bounds)))
        {
            double[] a = text.Bounds!;
            foreach (DrawingPlacement box in boxes.Where(b => text.Parent is not { } parent || parent.Oid != b.Locator.Oid || parent.Type != b.Locator.Type))
            {
                double[] b = box.Bounds!;
                bool touches = a[0] < b[2] - 1e-6 && a[2] > b[0] + 1e-6 && a[1] < b[3] - 1e-6 && a[3] > b[1] + 1e-6;
                bool inside = a[0] >= b[0] - 1e-6 && a[2] <= b[2] + 1e-6 && a[1] >= b[1] - 1e-6 && a[3] <= b[3] + 1e-6;
                if (touches && !inside)
                { c.Add("text_crosses_box", "warning", text.Location, [text.Locator, box.Locator], "Text runs across a location box border; keep it inside the box, as the official pages do, or outside it.", "eplan_placement_update", new() { ["locator"] = BridgeJson.FromObject(text.Locator) }); }
            }
        }
    }
}
