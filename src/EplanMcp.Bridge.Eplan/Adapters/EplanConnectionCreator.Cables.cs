using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanConnectionCreator
{
    public System.Action PrepareCables(string project, string page, JObject request, JObject plan)
    {
        AssertCurrent(project);
        Page target = Page(project, page);
        SymbolVariant variant = EplanProjects.ByPath(project).SymbolLibraries.Where(l => l.Name == "SPECIAL").SelectMany(l => l.Symbols)
            .Where(s => s.Type == Symbol.SymbolType.CableDefinitionLine).SelectMany(s => s.Variants).FirstOrDefault(v => v.VariantNr == 0)
            ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Load a SPECIAL cable definition line variant before creating cables.");
        return () =>
        {
            plan["_cableBefore"] = CableSnapshot(project, page);
            foreach (JToken definition in plan["cableDefinitions"]!)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                double[][] points = definition["points"]!.ToObject<double[][]>()!;
#pragma warning disable CA2000 // A39/A55: the project owns the cable, including after Abort.
                var cable = new Cable();
#pragma warning restore CA2000
                cable.Create(target, variant, new PointD(points[0][0], points[0][1]), new PointD(points[1][0], points[1][1]));
                cable.Name = (string)definition["designation"]!;
                using FunctionPropertyList properties = cable.Properties;
                properties[(Properties.Function)20040] = (string)definition["type_text"]!;
                properties[(Properties.Function)20042] = (string)definition["core_count"]!;
                properties[(Properties.Function)20043] = (string)definition["cross_section_mm2"]!;
                Check(CableDefinitionPlanner.SameName((string)definition["designation"]!, cable.Name, page)
                    && cable.Page.Name == page && cable.FunctionCategory == FunctionCategory.Cable);
                foreach (int id in new[] { 20040, 20042, 20043 })
                { using PropertyValue value = properties[(Properties.Function)id]; Check(!value.IsEmpty && (id == 20043 ? SameSection(value.ToString(), (double)definition["cross_section_mm2"]!) : value.ToString() == (string)definition[id == 20040 ? "type_text" : "core_count"]!)); }
            }
        };
    }
    private static Cable FindCable(string project, string page, JToken definition) => Page(project, page).AllPlacements.OfType<Cable>()
        .Single(c => CableDefinitionPlanner.SameName((string)definition["designation"]!, c.Name, page));
    private static Connection[] CableRuns(string project, string page, JObject request, JObject plan, JToken definition)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        Connection[] all = finder.GetConnections(null).Where(c => EplanConnectionReader.PageOf(c)?.Name == page).ToArray();
        Cable cable = FindCable(project, page, definition);
        return plan["coreImpact"]!.Where(r => (string?)r["designation"] == (string?)definition["designation"]).Select(row =>
        {
            JToken reference = row["connection"]!;
            string PinKey(JToken pin) => FormattableString.Invariant($"{pin["locator"]!["type"]}/{pin["locator"]!["oid"]}/{pin["pin_index"]}");
            string[] pins = reference["item_index"] is { } index
                ? [PinKey(request["items"]![(int)index]!["from"]!), PinKey(request["items"]![(int)index]!["to"]!)] : row["before"]!["pins"]!.Values<string>().Select(p => p!).ToArray();
            Connection run = all.Single(c => new HashSet<string?>(CablePins(c), StringComparer.Ordinal).SetEquals(pins));
            Check(reference["item_index"] is not null || run.StartPin?.ParentFunction is { } a && run.EndPin?.ParentFunction is { } b
                && cable.GraphicalConnections.Any(g => new HashSet<long> { g.StartSymbolRef.ObjectIdentifier, g.EndSymbolRef.ObjectIdentifier }.SetEquals([a.ObjectIdentifier, b.ObjectIdentifier])));
            if (reference["locator"] is { } locator)
            { Check(run.ObjectIdentifier == (long)locator["oid"]!); }
            if (plan["_cableBefore"] is JObject before)
            { Check(before[run.ObjectIdentifier.ToString(CultureInfo.InvariantCulture)] is JObject old && JToken.DeepEquals(old["pins"], JArray.FromObject(CablePins(run)))); }
            return run;
        }).ToArray();
    }
    private static string?[] CablePins(Connection run) => [run.StartPin?.ParentFunction is { } a ? Key(a, run.StartPin.Index) : null, run.EndPin?.ParentFunction is { } b ? Key(b, run.EndPin.Index) : null];
    public void BindCables(string project, string page, JObject request, JObject plan)
    {
        foreach (JToken definition in plan["cableDefinitions"]!)
        {
            Cable cable = FindCable(project, page, definition);
            using FunctionBasePropertyList name = cable.NameParts;
            foreach (Connection run in CableRuns(project, page, request, plan, definition))
            {
                Check(run.ConnectionDefPoints.Length > 0 && run.ConnectionDefPoints.All(p => p.CableDefinitionLine?.ObjectIdentifier == cable.ObjectIdentifier));
                foreach (ConnectionDefinitionPoint point in run.ConnectionDefPoints)
                { point.Name = cable.Name; point.VisibleName = cable.VisibleName; Check(point.Name == cable.Name && point.VisibleName == cable.VisibleName); }
                run.CableNameParts = name;
                using ConnectionPropertyList properties = run.Properties;
                Check(CableText(properties, 20005) == cable.Name);
            }
        }
    }
    public void AssignCores(string project, string page, JObject request, JObject plan)
    {
        foreach (JToken definition in plan["cableDefinitions"]!)
        {
            Cable cable = FindCable(project, page, definition);
            Connection[] runs = CableRuns(project, page, request, plan, definition);
            Check(cable.FunctionTemplates.Length == 0 && cable.CableConnections.Select(c => c.ObjectIdentifier).OrderBy(id => id).SequenceEqual(runs.Select(c => c.ObjectIdentifier).OrderBy(id => id)));
            JToken[] cores = definition["cores"]!.ToArray();
            for (int i = 0; i < runs.Length; i++)
            {
                using ConnectionPropertyList properties = runs[i].Properties;
                properties[(Properties.Connection)31011] = (string)cores[i]["label"]!;
                properties[(Properties.Connection)31004] = (string)cores[i]["colour"]!;
                properties[(Properties.Connection)31002] = (string)definition["cross_section_mm2"]!;
            }
            cable.AddLocalFunctionTemplates(runs);
            Connection[] templates = cable.FunctionTemplates.OfType<Connection>().ToArray();
            Check(templates.Length == runs.Length && templates.All(t => t.IsTemplate));
            using var service = new CableService();
            service.DoReassignWires(cable, templates, true, []);
        }
        CableDefinitionPlanner.VerifyCores(plan, ReadCores(project, page, request, plan), page, request);
    }
    public JArray ReadCores(string project, string page, JObject request, JObject plan)
    {
        var result = new JArray();
        var selected = new HashSet<long>();
        foreach (JToken definition in plan["cableDefinitions"]!)
        {
            Cable cable = FindCable(project, page, definition);
            Connection[] runs = CableRuns(project, page, request, plan, definition);
            Check(cable.CableConnections.Select(c => c.ObjectIdentifier).OrderBy(id => id).SequenceEqual(runs.Select(c => c.ObjectIdentifier).OrderBy(id => id)));
            using FunctionPropertyList properties = cable.Properties;
            using PropertyValue type = properties[(Properties.Function)20040];
            using PropertyValue count = properties[(Properties.Function)20042];
            using PropertyValue section = properties[(Properties.Function)20043];
            Check(!type.IsEmpty && !count.IsEmpty && !section.IsEmpty && count.ToString() == (string)definition["core_count"]! && SameSection(section.ToString(), (double)definition["cross_section_mm2"]!));
            foreach (Connection run in runs)
            {
                Check(selected.Add(run.ObjectIdentifier) && run.ConnectionDefPoints.Length > 0 && run.ConnectionDefPoints.All(p => p.CableDefinitionLine?.ObjectIdentifier == cable.ObjectIdentifier && p.Name == cable.Name && p.VisibleName == cable.VisibleName));
                using ConnectionPropertyList core = run.Properties;
                Check(CableText(core, 20005) == cable.Name && SameSection(CableText(core, 31002), (double)definition["cross_section_mm2"]!));
                result.Add(new JObject { ["designation"] = cable.Name, ["type"] = type.ToString(), ["oid"] = run.ObjectIdentifier, ["pins"] = JArray.FromObject(CablePins(run)), ["label"] = CableText(core, 31011), ["colour"] = CableText(core, 31004) });
            }
        }
        JObject after = CableSnapshot(project, page), before = (JObject)plan["_cableBefore"]!.DeepClone();
        foreach (long id in selected)
        { after.Remove(id.ToString(CultureInfo.InvariantCulture)); before.Remove(id.ToString(CultureInfo.InvariantCulture)); }
        Check(JToken.DeepEquals(before, after));
        return result;
    }
    private static JObject CableSnapshot(string project, string page)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        var result = new JObject();
        foreach (Connection run in finder.GetConnections(null).Where(c => EplanConnectionReader.PageOf(c)?.Name == page))
        {
            using ConnectionPropertyList properties = run.Properties;
            result[run.ObjectIdentifier.ToString(CultureInfo.InvariantCulture)] = new JObject { ["pins"] = JArray.FromObject(CablePins(run)), ["cable"] = CableText(properties, 20005), ["label"] = CableText(properties, 31011), ["colour"] = CableText(properties, 31004), ["section"] = CableText(properties, 31002) };
        }
        return result;
    }
    private static string? CableText(ConnectionPropertyList properties, int number)
    { using AnyPropertyId id = number; using PropertyValue value = properties[id]; return value.IsEmpty ? null : value.ToString(); }
    private static bool SameSection(string? text, double expected) => double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double actual) && actual == expected;
    private static void Check(bool valid) { if (!valid) { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Cable definition, membership or core readback differs from the plan."); } }
}
