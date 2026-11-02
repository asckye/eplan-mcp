using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanPageReviewReader : PageReviewOperation.IReader
{
    public (ConnectionCreatePlanner.Scene Scene, JObject Actual) Read(string project, string page, JObject request)
    {
        OpenProject target = ProjectResolver.Resolve(new EplanProjectCatalog().ListOpenProjects(), project);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(target.Path));
        Page[] pages = EplanNativePreconditions.Valid(finder.GetPages(null)).ToArray();
        Page native = pages.SingleOrDefault(p => string.Equals(p.Name, page, StringComparison.Ordinal)) ?? throw new BridgeErrorException(ErrorReason.PageNotFound, "Page not found; use eplan_page_list.");
        var scene = new EplanConnectionCreator(readSymbolBounds: false, allowReadOnly: true).Read(target.Path, page);
        // Local 2026 XML: DynamicConnectionLine.GetGraphics returns the line representation; native objects stay in this read. Large verifies it.
        foreach (DynamicConnectionLine line in EplanNativePreconditions.Valid(native.AllPlacements).OfType<DynamicConnectionLine>())
        { if (line.GetGraphics() is global::Eplan.EplApi.DataModel.Graphics.Line graphic) { scene.Graphical.Add([[graphic.StartPoint.X, graphic.StartPoint.Y], [graphic.EndPoint.X, graphic.EndPoint.Y]]); } }
        var actual = new JObject { ["devices"] = new JArray(), ["connections"] = new JArray(), ["cables"] = new JArray(), ["interruptions"] = new JArray(), ["texts"] = new JArray(), ["messages"] = new JArray() };
        foreach (DrawingPlacement p in scene.Page.Placements.Where(p => p.Parent is null))
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            JObject Row() => new() { ["position"] = new JArray(p.Location), ["locator"] = BridgeJson.FromObject(p.Locator) };
            if (EplanNativePreconditions.Valid(native.AllPlacements).OfType<Function>().FirstOrDefault(f => f.ObjectIdentifier == p.Locator.Oid) is { } f && f is not Cable)
            { JObject row = Row(); row["tag"] = f.Name; row["library"] = f.SymbolVariant?.SymbolLibraryName; row["symbol"] = f.SymbolVariant?.SymbolName; row["symbol_variant"] = f.SymbolVariant?.VariantNr; row["pins"] = JObject.FromObject(f.Pins.ToDictionary(p => p.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.Name ?? "")); row["placement"] = BridgeJson.FromObject(p); ((JArray)actual["devices"]!).Add(row); }
            if (p.Kind == "Text")
            { JObject row = Row(); row["text"] = p.Properties["text"]; row["angle"] = p.Properties.TryGetValue("angle", out string? angle) && double.TryParse(angle, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double degrees) ? degrees : null; ((JArray)actual["texts"]!).Add(row); }
        }
        CableDefinitionPlanner.Inventory inventory = scene.ReadCables!();
        foreach (Connection c in EplanNativePreconditions.Valid(finder.GetConnections(null)).Where(c => EplanConnectionReader.PageOf(c)?.Name == page))
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            static JToken? End(Pin? pin) => pin?.ParentFunction is { } f ? new JObject { ["device"] = f.Name, ["pin"] = pin.Name ?? "" } : null;
            CableDefinitionPlanner.Wire wire = inventory.Wires.FirstOrDefault(w => w.Locator.Oid == c.ObjectIdentifier) ?? EplanConnectionCreator.ReadCableWire(target.Path, page, c);
            wire.Path = wire.Points.Length == 2 && wire.Points.All(p => p.Length == 2) ? CableDefinitionPlanner.DrawnPath(scene, wire.Points[0], wire.Points[1]) : null;
            ((JArray)actual["connections"]!).Add(ConnectionRow(wire, End(c.StartPin), End(c.EndPin), c.ConnectionDefPoints.Select(p => p.ObjectIdentifier)));
        }
        foreach (Cable cable in EplanNativePreconditions.Valid(native.AllPlacements).OfType<Cable>())
        {
            DrawingPlacement placement = scene.Page.Placements.First(p => p.Parent is null && p.Kind == "Cable" && p.Locator.Oid == cable.ObjectIdentifier);
            double[][]? points = null;
            try
            {
                // A55: the cable's logical area gives its definition line endpoints.
                var area = cable.GetLogicalArea();
                points = [[area.LowerLeft.X, area.LowerLeft.Y], [area.UpperRight.X, area.UpperRight.Y]];
            }
            catch (Exception ex) when (ex is global::Eplan.EplApi.Base.BaseException or InvalidOperationException)
            { WriteExceptionDiagnostics.Report(ex); }
            ((JArray)actual["cables"]!).Add(CableRow(cable.Name, placement.Locator, cable.CableConnections.Select(w => w.ObjectIdentifier), (JArray)actual["connections"]!, placement.Location, points));
        }
        bool Enabled(string check) => request["checks"] is not JArray checks || checks.Values<string>().Contains(check, StringComparer.Ordinal);
        if (Enabled("interruption_pairing") || (request["expected"] as JObject)?["interruptions"] is not null)
        {
            foreach (InterruptionPoint p in EplanNativePreconditions.Valid(finder.GetInterruptionPoints(null)))
            { OperationContext.Current.ThrowIfCancellationRequested(); ((JArray)actual["interruptions"]!).Add(new JObject { ["name"] = p.Name, ["page"] = p.Page.Name, ["pageOrder"] = Array.FindIndex(pages, page => page.Name == p.Page.Name), ["position"] = new JArray(p.Location.X, p.Location.Y), ["locator"] = p.Page.Name == page ? BridgeJson.FromObject(scene.Page.Placements.First(d => d.Locator.Oid == p.ObjectIdentifier).Locator) : null }); }
        }
        if (Enabled("eplan_messages"))
        {
            foreach (ProjectMessageRecord m in new EplanProjectMessageReader().ReadMessages(target.Path).Where(m => !m.Done && m.Page == page))
            { ((JArray)actual["messages"]!).Add(new JObject { ["text"] = m.Text, ["severity"] = m.Category == "Error" ? "error" : m.Category == "Warning" ? "warning" : "info", ["objects"] = new JArray(scene.Page.Placements.Where(p => p.Properties.TryGetValue("name", out string? name) && name == m.ObjectName).Select(p => BridgeJson.FromObject(p.Locator))) }); }
        }
        return (scene, actual);
    }
}
