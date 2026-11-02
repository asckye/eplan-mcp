using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal static class EplanLayout
{
    internal static LayoutGuard.Model Read(string project, string page)
    {
        ConnectionCreatePlanner.Scene scene = new EplanConnectionCreator(false).Read(project, page);
        LayoutGuard.Model model = LayoutGuard.From(scene);
        model.ReadActual = () => Measure(project, page, []);
        return model;
    }
    internal static LayoutGuard.Model Measure(string project, string page, IReadOnlyList<Placement> created)
    {
        LayoutGuard.Model model = LayoutGuard.From(new EplanConnectionCreator(false).Read(project, page, created));
        model.Lines = PlacementMovePlanner.Autoconnect(model.Pins).Values.Select(s => new LayoutGuard.Segment { Points = s }).ToList();
        return model;
    }
    internal static LayoutGuard.Options Config()
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "config.json");
        return File.Exists(file) ? JObject.Parse(File.ReadAllText(file))["layout"]?.ToObject<LayoutGuard.Options>() ?? new() : new();
    }
    internal static IReadOnlyList<DrawingPlacement> Device(string project, DevicePlaceArgs args, LayoutGuard.Model model)
    {
        if (args.LocationBox is not null)
        { return [LayoutGuard.Proposed("LocationBox", [args.X, args.Y], [args.X, args.Y, args.CornerX!.Value, args.CornerY!.Value])]; }
        string? library = args.TerminalStrip is null ? args.Library : "IEC_symbol", name = args.TerminalStrip is null ? args.Symbol : "X";
        var variant = EplanProjects.ByPath(project).SymbolLibraries.Where(l => string.Equals(l.Name, library, StringComparison.OrdinalIgnoreCase))
            .SelectMany(l => l.Symbols).Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s.Variants).FirstOrDefault(v => v.VariantNr == (args.SymbolVariant ?? 0));
        double[] outline = variant is null ? [] : EplanConnectionCreator.Outline(variant);
        DrawingPlacement symbol = LayoutGuard.Proposed("Function", [args.X, args.Y], DrawingCoordinates.ValidBounds(outline)
            ? outline.Select((n, i) => n + (i % 2 == 0 ? args.X : args.Y)).ToArray() : null);
        if (variant is not null)
        {
            int index = 0;
            foreach (var pin in variant.ConnectionPoints)
            { model.Pins[FormattableString.Invariant($"0/-1/{index++}")] = new() { Location = [args.X + pin.Location.X, args.Y + pin.Location.Y], Direction = pin.Direction.ToString() }; }
            List<LayoutGuard.Segment> before = model.Lines;
            model.Lines = PlacementMovePlanner.Autoconnect(model.Pins).Values.Select(s => new LayoutGuard.Segment
            { Points = s, Changed = !before.Any(b => b.Points[0].SequenceEqual(s[0]) && b.Points[1].SequenceEqual(s[1]) || b.Points[0].SequenceEqual(s[1]) && b.Points[1].SequenceEqual(s[0])) }).ToList();
            if (variant.SymbolName == "PLCC")
            { symbol.Properties["layout_container"] = "true"; }
        }
        return [symbol, .. (args.DisplayProperties ?? []).Select((p, i) => new DrawingPlacement
        {
            Kind = "PropertyPlacement", Locator = new() { Oid = -i - 2 }, Parent = symbol.Locator,
            Location = [args.X + p.OffsetXmm, args.Y + p.OffsetYmm],
            Properties = new() { ["height_mm"] = "2.5", ["property_id"] = p.PropertyId.ToString(System.Globalization.CultureInfo.InvariantCulture) }
        })];
    }
}
