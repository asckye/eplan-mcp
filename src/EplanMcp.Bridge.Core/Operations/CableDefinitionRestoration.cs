using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Live T8b: undo can renumber identifiers; verify restored business content independently of planning.</summary>
internal static class CableDefinitionRestoration
{
    internal static JObject Capture(ConnectionCreatePlanner.Scene scene)
    {
        CableDefinitionPlanner.Inventory cables = scene.ReadCables?.Invoke()
            ?? throw new InvalidOperationException("Restoration cable inventory is unavailable.");
        if (!scene.Complete || !cables.Complete)
        { throw new InvalidOperationException("Restoration page or cable inventory is incomplete."); }
        string Placement(DrawingPlacement p) => BridgeJson.FromObject(new { p.Locator.Type, p.Kind, p.Location, p.Bounds }).ToString(Formatting.None);
        var owners = scene.Page.Placements.Where(p => p.Parent is null).ToDictionary(p => FormattableString.Invariant($"{p.Locator.Type}/{p.Locator.Oid}"), Placement, StringComparer.Ordinal);
        var pins = scene.Pins.ToDictionary(p => p.Key, p =>
        {
            string[] parts = p.Key.Split('/');
            return new JArray(owners[string.Join("/", parts.Take(2))], parts.Last(), BridgeJson.FromObject(p.Value)).ToString(Formatting.None);
        }, StringComparer.Ordinal);
        string? Pin(string? key) => key is null ? null : pins.TryGetValue(key, out string? value) ? value
            : throw new InvalidOperationException("Restoration endpoint has no pin geometry: " + key);
        JArray Sorted(IEnumerable<string?> values) => new(values.OrderBy(v => v, StringComparer.Ordinal));
        string Pair(IEnumerable<string?> keys) => Sorted(keys.Select(Pin)).ToString(Formatting.None);
        return new JObject
        {
            ["page"] = BridgeJson.FromObject(new { scene.Page.Project, scene.Page.Page, scene.Page.GridSize, scene.Page.PageSize, scene.PageType }),
            ["placements"] = Sorted(scene.Page.Placements.Select(Placement)),
            ["pins"] = Sorted(pins.Values),
            ["graphical"] = Sorted(scene.Graphical.Select(s => Sorted(s.Select(p => new JArray(p).ToString(Formatting.None))).ToString(Formatting.None)).Distinct(StringComparer.Ordinal)),
            ["endpoints"] = Sorted(scene.Logical.Select(c => Pair([c.StartDevice, c.EndDevice]))),
            ["cableNames"] = Sorted(cables.Names),
            ["cores"] = Sorted(cables.Wires.Select(w => BridgeJson.FromObject(new
            {
                pins = Pair(w.Pins),
                cable = CableDefinitionPlanner.HasCable(w.Cable) ? w.Cable : null,
                w.Label,
                w.Colour,
                w.CrossSection
            }).ToString(Formatting.None)))
        };
    }
    internal static void Verify(JObject before, JObject after)
    {
        foreach (JProperty property in before.Properties())
        {
            if (!JToken.DeepEquals(property.Value, after[property.Name]))
            { throw new InvalidOperationException($"Restoration {property.Name} differs: expected {property.Value.ToString(Formatting.None)}; actual {after[property.Name]?.ToString(Formatting.None)}."); }
        }
    }
}
