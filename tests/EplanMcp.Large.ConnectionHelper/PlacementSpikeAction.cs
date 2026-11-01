using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Native observations are independent of the product planner, so failed assumptions remain visible.</summary>
public sealed class PlacementSpikeAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargePlacementSpike"; Ordinal = 20; return true; }
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
            if (!string.Equals(project.ProjectFullName, Path.Combine(home, "scratch", "writes", "LargeWrite"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(project.ProjectFullName, (string?)input["project"], StringComparison.OrdinalIgnoreCase)
                || !((string?)input["page"] ?? "").StartsWith("=GAA+A1&EFS1/", StringComparison.Ordinal))
            { throw new InvalidOperationException("Only an explicit new page of current scratch LargeWrite is permitted."); }
            Page Page() => project.Pages.Single(p => p.Name == (string)input["page"]!);
            result["before"] = Read(project, Page());
            // A52: discover library variants before entering SafetyPoint.
            SymbolVariant Variant(string symbol) => project.SymbolLibraries.Single(l => l.Name == "SPECIAL").Symbols.Single(s => s.Name == symbol).Variants.OrderBy(v => v.VariantNr).First();
            (SymbolVariant Variant, double X)[] seedSymbols = (string?)input["operation"] == "seed" ? [(Variant("TLRO"), 280), (Variant("PCP"), 340)] : [];
            if ((string?)input["operation"] != "read")
            {
                using var scope = new TransactionScope("Placement edit spike");
                using var edit = new Edit();
                try
                {
                    Page page = Page();
                    Placement[] roots = page.AllPlacements.Where(p => p is not PropertyPlacement && input["oids"]!.Values<long>().Contains(p.ObjectIdentifier)).ToArray();
                    if ((string?)input["operation"] == "seed")
                    {
                        foreach ((SymbolVariant variant, double x) in seedSymbols)
                        {
#pragma warning disable CA2000 // A39: EPLAN owns created placements, including rollback paths.
                            SymbolReference.Create(variant, page).Location = new PointD(x, 200);
#pragma warning restore CA2000
                        }
                        foreach (TextBase text in page.AllPlacements.OfType<TextBase>())
                        { text.Rotation = 0; }
                        foreach (SymbolReference symbol in page.AllPlacements.OfType<SymbolReference>())
                        {
                            // B12: variant property placements belong to the read-only symbol library.
                            symbol.UseLocalPropertyPlacements = true;
                            foreach (PropertyPlacement text in symbol.PropertyPlacements)
                            { text.Rotation = 0; }
                        }
                    }
                    foreach (Placement root in roots)
                    {
                        switch ((string)input["operation"]!)
                        {
                            case "delete":
                                root.Remove();
                                break;
                            case "copy":
                                Placement copied = root.CopyTo(page);
                                if (copied is Line line)
                                { line.StartPoint = new PointD(line.StartPoint.X, line.StartPoint.Y - 100); line.EndPoint = new PointD(line.EndPoint.X, line.EndPoint.Y - 100); }
                                else
                                { copied.Location = new PointD(root.Location.X, root.Location.Y - 100); }
                                break;
                            case "rotate":
                                edit.Rotate([root], input["pivot"] is JArray pivot ? new PointD((double)pivot[0]!, (double)pivot[1]!) : root.Location, Math.PI / 2);
                                break;
                            case "mirror":
                                bool horizontal = (string?)input["axis"] == "horizontal";
                                edit.Mirror([root], new PointD(horizontal ? 0 : 210, horizontal ? 150 : 0), new PointD(horizontal ? 1 : 210, horizontal ? 150 : 1), false);
                                break;
                        }
                    }
                    result["during"] = Read(project, Page());
                    if ((bool?)input["rollback"] == true)
                    { scope.Rollback(); }
                    else
                    { scope.Commit(); result["afterCommit"] = Read(project, Page()); scope.CloseUndo(); }
                }
                catch { scope.Rollback(); throw; }
            }
            result["after"] = Read(project, Page());
            result["success"] = true;
        }
        catch (Exception error) { result["error"] = error.ToString(); }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }
    private static JObject Read(Project project, Page page)
    {
        object? End(Pin? pin) => pin?.ParentFunction is { } f ? new { oid = f.ObjectIdentifier, name = f.Name, pin = pin.Name } : null;
        JObject Placement(Placement p)
        {
            using MultiLangString? text = p is PropertyPlacement property ? property.Contents : p is Text t ? t.Contents : null;
            PointD location = p.Location;
            return new JObject
            {
                ["oid"] = p.ObjectIdentifier,
                ["kind"] = p.GetType().Name,
                ["location"] = new JArray(location.X, location.Y),
                ["bounds"] = new JArray((p is PropertyPlacement pp ? pp.GetBoundingBox(false) : p.GetBoundingBox()).SelectMany(v => new[] { v.X, v.Y })),
                ["angle"] = p is TextBase tb ? tb.Rotation : p is Rectangle r ? r.Angle : null,
                ["name"] = (p as Function)?.Name,
                ["text"] = text?.GetAsString(),
                ["points"] = p is Line line ? new JArray(new JArray(line.StartPoint.X, line.StartPoint.Y), new JArray(line.EndPoint.X, line.EndPoint.Y)) : null,
                ["pins"] = p is SymbolReference symbol ? new JArray(symbol.GraphicalConnectionPoints.Select(pin => new JObject { ["location"] = new JArray(location.X + pin.Location.X, location.Y + pin.Location.Y), ["direction"] = pin.Direction.ToString() })) : new JArray(),
                ["children"] = p is SymbolReference owner ? new JArray(owner.PropertyPlacements.Select(Placement)) : new JArray()
            };
        }
        using var finder = new DMObjectsFinder(project);
        return new JObject
        {
            ["objects"] = new JArray(page.AllPlacements.Where(p => p is not PropertyPlacement).OrderBy(p => p.ObjectIdentifier).Select(Placement)),
            ["functions"] = new JArray(finder.GetFunctions(null).OrderBy(f => f.ObjectIdentifier).Select(f => new JObject { ["oid"] = f.ObjectIdentifier, ["name"] = f.Name, ["page"] = f.Page?.Name })),
            ["connections"] = new JArray(finder.GetConnections(null).Where(c => c.Page?.Name == page.Name).OrderBy(c => c.ObjectIdentifier).Select(c => JObject.FromObject(new { oid = c.ObjectIdentifier, start = End(c.StartPin), end = End(c.EndPin) })))
        };
    }
}
