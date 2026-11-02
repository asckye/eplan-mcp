using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Deliberately defective drawings on explicit empty LargeWrite pages; repairs use product tools.</summary>
internal static class ReviewFixture
{
    internal static void Create(Project project, Page[] pages, JObject request)
    {
        if (pages.Any(p => p.AllPlacements.Length != 0))
        { throw new InvalidOperationException("Review fixtures require new empty pages."); }
        Page page = pages[0];
        double grid = page.GridSize;
        string prefix = "=GAA+A1-R" + page.Name.Split('/').Last();
        SymbolVariant Variant(string name, string direction) => project.SymbolLibraries.Where(l => l.Name == "IEC_symbol").SelectMany(l => l.Symbols)
            .Where(s => s.Name == name).SelectMany(s => s.Variants).Where(v => v.ConnectionPoints.Any(p => p.Direction.ToString() == direction)).OrderBy(v => v.VariantNr).First();
        void Device(string suffix, double x, double y, string facing)
        {
            SymbolVariant variant = Variant("BAT", facing);
            PinBase pin = variant.ConnectionPoints.First(p => p.Direction.ToString() == facing);
#pragma warning disable CA2000 // A39/A55: EPLAN owns the function, including on Abort.
            var function = new Function();
#pragma warning restore CA2000
            function.Create(page, variant);
            function.Name = prefix + suffix;
            function.Location = new PointD(x * grid - pin.Location.X, y * grid - pin.Location.Y);
        }
        void Point(double x)
        {
            SymbolVariant variant = project.SymbolLibraries.Where(l => l.Name == "SPECIAL").SelectMany(l => l.Symbols).Where(s => s.Name == "BPF").SelectMany(s => s.Variants).First();
#pragma warning disable CA2000 // A39/A55: EPLAN owns the created symbol.
            InterruptionPoint point = (InterruptionPoint)SymbolReference.Create(variant, page);
#pragma warning restore CA2000
            point.Name = prefix + "I1";
            point.Location = new PointD(x * grid, 40 * grid);
        }
        switch ((string?)request["scenario"])
        {
            case "message":
                // Local 2026 XML: creation type 2 marks a stored offline-check message; verification replaces it.
                new Eplan.EplApi.EServices.PrjMessagesCollection(project).AddMessage(Eplan.EplApi.EServices.IMessage.Region.Connections, 1, "", page, false, null!, "Review fixture", 2);
                break;
            case "dangling":
#pragma warning disable CA2000 // A39/A55: EPLAN owns the dynamic wire.
                var wire = new DynamicConnectionLine();
#pragma warning restore CA2000
                wire.Create(page);
                wire.SetGraphics(new PointD(30 * grid, 40 * grid), new PointD(50 * grid, 40 * grid));
                break;
            case "open_pin":
                Device("A", 30, 40, "Down");
                break;
            case "networks":
                Device("A", 30, 55, "Down");
                Device("B", 30, 25, "Up");
                Device("C", 50, 55, "Down");
                Device("D", 50, 25, "Up");
                break;
            case "pairing":
                Point(20);
                Point(40);
                Point(60);
                Point(80);
                break;
            case "cable":
                Device("A", 30, 55, "Down");
                Device("B", 30, 25, "Up");
                Device("C", 50, 40, "Left");
                SymbolVariant tee = project.SymbolLibraries.Where(l => l.Name == "SPECIAL").SelectMany(l => l.Symbols).Where(s => s.Name == "TOUR").SelectMany(s => s.Variants)
                    .First(v => new HashSet<string>(v.ConnectionPoints.Select(p => p.Direction.ToString()), StringComparer.Ordinal).SetEquals(["Up", "Down", "Right"]));
#pragma warning disable CA2000 // A39/A55: EPLAN owns the created node.
                SymbolReference node = SymbolReference.Create(tee, page);
#pragma warning restore CA2000
                node.Location = new PointD(30 * grid, 40 * grid);
                break;
            case "text":
                using (var contents = new MultiLangString())
                {
                    contents.AddString(ISOCode.Language.L___, "Review fixture");
                    for (int i = 0; i < 2; i++)
                    {
#pragma warning disable CA2000 // A39: EPLAN owns the created text, including on Abort.
                        var text = new Text();
#pragma warning restore CA2000
                        text.Create(page, contents, 2.5);
                        text.Location = new PointD(20 * grid + 0.25, 40 * grid);
                    }
                }
                break;
            default:
                throw new InvalidOperationException("Unknown review fixture scenario.");
        }
    }
}
