using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>A55 staged definition/binding, followed by native assignment from independent fictional seed wires.</summary>
internal static class SpikeCable
{
    internal static void Define(Project project, Page page, JObject request)
    {
        string name = (string)request["cable"]!;
        using var finder = new DMObjectsFinder(project);
        if (finder.GetFunctions(null).Any(f => f.Name == name))
        { throw new InvalidOperationException("Use an unused spike cable name."); }
        SymbolVariant variant = project.SymbolLibraries.Where(l => l.Name == "SPECIAL").SelectMany(l => l.Symbols)
            .Where(s => s.Type == Symbol.SymbolType.CableDefinitionLine).SelectMany(s => s.Variants).First(v => v.VariantNr == 0);
        double[][] line = request["line"]!.ToObject<double[][]>()!;
#pragma warning disable CA2000 // A39/A55: EPLAN owns new storable objects, including on Abort.
        var cable = new Cable();
#pragma warning restore CA2000
        cable.Create(page, variant, new PointD(line[0][0], line[0][1]), new PointD(line[1][0], line[1][1]));
        cable.Name = name;
        using FunctionPropertyList properties = cable.Properties;
        properties[(Properties.Function)20040] = "D1 fictional 3-core cable";
        properties[(Properties.Function)20042] = "3";
        properties[(Properties.Function)20043] = "0.5";
    }

    internal static Cable Find(Page page, JObject request) => page.AllPlacements.OfType<Cable>().Single(c => c.Name == (string)request["cable"]!);

    internal static void Bind(Project project, Page page, JObject request)
    {
        Cable cable = Find(page, request);
        using FunctionBasePropertyList name = cable.NameParts;
        using var finder = new DMObjectsFinder(project);
        foreach (Connection wire in finder.GetConnections(null).Where(c => c.Page?.Name == page.Name))
        {
            ConnectionDefinitionPoint[] crossed = wire.ConnectionDefPoints.Where(p => p.CableDefinitionLine?.ObjectIdentifier == cable.ObjectIdentifier).ToArray();
            if (crossed.Length == 0)
            { continue; }
            foreach (ConnectionDefinitionPoint point in crossed)
            {
                point.Name = cable.Name;
                point.VisibleName = cable.VisibleName;
            }
            wire.CableNameParts = name;
        }
    }

    internal static void Templates(Project project, Page[] pages, JObject request)
    {
        if (pages.Length != 2)
        { throw new InvalidOperationException("Supply a separate seed-wire page."); }
        Cable cable = Find(pages[0], request);
        using var finder = new DMObjectsFinder(project);
        Connection[] seeds = finder.GetConnections(null).Where(c => c.Page?.Name == pages[1].Name)
            .OrderBy(c => c.StartPin.ParentFunction.Location.X).ToArray();
        if (seeds.Length != 3 || cable.FunctionTemplates.Length != 0)
        { throw new InvalidOperationException("Expected three independent seed wires and a cable without templates."); }
        string[] colours = ["RD", "BK", "BU"];
        for (int i = 0; i < seeds.Length; i++)
        {
            using ConnectionPropertyList properties = seeds[i].Properties;
            properties[(Properties.Connection)31011] = (i + 1).ToString(CultureInfo.InvariantCulture);
            properties[(Properties.Connection)31004] = colours[i];
            properties[(Properties.Connection)31002] = "0.5";
        }
        // Fix the available core pool independently of the star's observed logical/graphical counts. In particular,
        // never stamp labels or colours onto star connections before asking EPLAN to assign the templates.
        cable.AddLocalFunctionTemplates(seeds);
    }

    internal static void Assign(Page page, JObject request)
    {
        Cable cable = Find(page, request);
        Connection[] templates = cable.FunctionTemplates.OfType<Connection>().ToArray();
        using var service = new CableService();
        service.DoReassignWires(cable, templates, true, []);
    }
}
