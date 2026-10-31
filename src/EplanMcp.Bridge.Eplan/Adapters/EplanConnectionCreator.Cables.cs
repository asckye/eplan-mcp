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
        _ = Page(project, page);
        _ = CableVariant(project);
        return () =>
        {
            // A55: the closure retains managed inputs only; native handles belong to this stage.
            Page target = Page(project, page);
            SymbolVariant variant = CableVariant(project);
            plan["_cableBefore"] = CableSnapshot(project, page);
            foreach (JToken definition in plan["cableDefinitions"]!)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                double[][] points = definition["points"]!.ToObject<double[][]>()!;
#pragma warning disable CA2000 // A39/A55: the project owns the cable, including after Abort.
                var cable = new Cable();
#pragma warning restore CA2000
                cable.Create(target, variant, new PointD(points[0][0], points[0][1]), new PointD(points[1][0], points[1][1]));
                cable.Name = (string?)definition["name"] ?? (string)definition["designation"]!;
                using FunctionPropertyList properties = cable.Properties;
                properties[(Properties.Function)20040] = (string)definition["type_text"]!;
                properties[(Properties.Function)20042] = (string)definition["core_count"]!;
                properties[(Properties.Function)20043] = (string)definition["cross_section_mm2"]!;
            }
        };
    }
    private static SymbolVariant CableVariant(string project) => EplanProjects.ByPath(project).SymbolLibraries.Where(l => l.Name == "SPECIAL").SelectMany(l => l.Symbols)
        .Where(s => s.Type == Symbol.SymbolType.CableDefinitionLine).SelectMany(s => s.Variants).FirstOrDefault(v => v.VariantNr == 0)
        ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Load a SPECIAL cable definition line variant before creating cables.");
    public void VerifyCables(string project, string page, JObject request, JObject plan)
    {
        foreach (JToken definition in plan["cableDefinitions"]!)
        {
            Cable cable = FindCable(project, page, definition);
            double[][] points = definition["points"]!.ToObject<double[][]>()!;
            Check(CableDefinitionPlanner.SameName((string)definition["designation"]!, cable.Name, page), $"designation: expected '{definition["designation"]}'; actual '{cable.Name}'");
            Check(cable.Page.Name == page, $"page: expected '{page}'; actual '{cable.Page.Name}'");
            Check(cable.FunctionCategory == FunctionCategory.Cable, $"category: actual '{cable.FunctionCategory}'");
            RectangleD area = cable.GetLogicalArea();
            double[] actualPoints = [area.LowerLeft.X, area.LowerLeft.Y, area.UpperRight.X, area.UpperRight.Y];
            double[] expectedPoints = [Math.Min(points[0][0], points[1][0]), Math.Min(points[0][1], points[1][1]), Math.Max(points[0][0], points[1][0]), Math.Max(points[0][1], points[1][1])];
            Check(actualPoints.SequenceEqual(expectedPoints), $"definition line points: expected {new JArray(expectedPoints)}; actual {new JArray(actualPoints)}");
            using FunctionPropertyList properties = cable.Properties;
            foreach (int id in new[] { 20040, 20042, 20043 })
            {
                using PropertyValue value = properties[(Properties.Function)id];
                string field = id == 20040 ? "type_text" : id == 20042 ? "core_count" : "cross_section_mm2";
                string? actual = value.IsEmpty ? null : value.ToString();
                Check(id == 20043 ? SameSection(actual, (double)definition[field]!) : actual == (string)definition[field]!, $"{field}: expected '{definition[field]}'; actual '{actual ?? "<empty>"}'");
            }
        }
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
                // Customer rehearsal: when a T-node splits the core's wire, the crossed drawn segment runs from one of the
                // connection's devices to the node, not device to device; the plan already pinned the crossing to this core.
                && cable.GraphicalConnections.Any(g => new HashSet<long> { g.StartSymbolRef.ObjectIdentifier, g.EndSymbolRef.ObjectIdentifier }.SetEquals([a.ObjectIdentifier, b.ObjectIdentifier])
                    || g.StartSymbolRef.ObjectIdentifier == a.ObjectIdentifier || g.StartSymbolRef.ObjectIdentifier == b.ObjectIdentifier
                    || g.EndSymbolRef.ObjectIdentifier == a.ObjectIdentifier || g.EndSymbolRef.ObjectIdentifier == b.ObjectIdentifier),
                $"graphical membership: expected pins {new JArray(pins)}; actual {JArray.FromObject(cable.GraphicalConnections.Select(g => new[] { g.StartSymbolRef.ObjectIdentifier, g.EndSymbolRef.ObjectIdentifier }))}");
            if (reference["locator"] is { } locator)
            { Check(run.ObjectIdentifier == (long)locator["oid"]!, FormattableString.Invariant($"connection identity: expected {locator["oid"]}; actual {run.ObjectIdentifier}")); }
            if (plan["_cableBefore"] is JObject before)
            { Check(before[run.ObjectIdentifier.ToString(CultureInfo.InvariantCulture)] is JObject old && JToken.DeepEquals(old["pins"], JArray.FromObject(CablePins(run))), $"preserved pins: actual {JArray.FromObject(CablePins(run))}"); }
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
                Check(run.ConnectionDefPoints.Length > 0, $"definition points: actual {run.ConnectionDefPoints.Length.ToString(CultureInfo.InvariantCulture)}");
                foreach (ConnectionDefinitionPoint point in run.ConnectionDefPoints)
                {
                    Check(point.CableDefinitionLine?.ObjectIdentifier == cable.ObjectIdentifier, $"definition point membership: actual '{point.CableDefinitionLine?.Name ?? "<none>"}'");
                    point.Name = cable.Name;
                    point.VisibleName = cable.VisibleName;
                }
                run.CableNameParts = name;
            }
        }
    }
    public void VerifyBindings(string project, string page, JObject request, JObject plan)
    {
        foreach (JToken definition in plan["cableDefinitions"]!)
        {
            Cable cable = FindCable(project, page, definition);
            foreach (Connection run in CableRuns(project, page, request, plan, definition))
            {
                Check(run.ConnectionDefPoints.Length > 0, $"definition points: actual {run.ConnectionDefPoints.Length.ToString(CultureInfo.InvariantCulture)}");
                foreach (ConnectionDefinitionPoint point in run.ConnectionDefPoints)
                {
                    Check(point.CableDefinitionLine?.ObjectIdentifier == cable.ObjectIdentifier, $"definition point membership: actual '{point.CableDefinitionLine?.Name ?? "<none>"}'");
                    Check(CableDefinitionPlanner.SameName(cable.Name, point.Name, page), $"definition point designation: expected '{cable.Name}'; actual '{point.Name}'");
                    Check(point.VisibleName == cable.VisibleName, $"definition point visible name: expected '{cable.VisibleName}'; actual '{point.VisibleName}'");
                }
                using ConnectionPropertyList properties = run.Properties;
                Check(CableDefinitionPlanner.SameName(cable.Name, CableText(properties, 20005) ?? "", page), $"bound membership: expected '{cable.Name}'; actual '{CableText(properties, 20005) ?? "<empty>"}'");
            }
        }
    }
    public void AssignCores(string project, string page, JObject request, JObject plan)
    {
        foreach (JToken definition in plan["cableDefinitions"]!)
        {
            Cable cable = FindCable(project, page, definition);
            Connection[] runs = CableRuns(project, page, request, plan, definition);
            Check(cable.FunctionTemplates.Length == 0, $"initial templates: actual {cable.FunctionTemplates.Length.ToString(CultureInfo.InvariantCulture)}");
            CheckMembership(cable, runs);
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
            using var service = new CableService();
            service.DoReassignWires(cable, templates, true, []);
        }
    }
    public JArray ReadCores(string project, string page, JObject request, JObject plan)
    {
        var result = new JArray();
        var selected = new HashSet<long>();
        foreach (JToken definition in plan["cableDefinitions"]!)
        {
            Cable cable = FindCable(project, page, definition);
            Connection[] runs = CableRuns(project, page, request, plan, definition);
            CheckMembership(cable, runs);
            Connection[] templates = cable.FunctionTemplates.OfType<Connection>().ToArray();
            Check(templates.Length == runs.Length, FormattableString.Invariant($"template count: expected {runs.Length}; actual {templates.Length}"));
            Check(templates.All(t => t.IsTemplate), $"template flags: actual {JArray.FromObject(templates.Select(t => t.IsTemplate))}");
            using FunctionPropertyList properties = cable.Properties;
            using PropertyValue type = properties[(Properties.Function)20040];
            using PropertyValue count = properties[(Properties.Function)20042];
            using PropertyValue section = properties[(Properties.Function)20043];
            string? actualType = type.IsEmpty ? null : type.ToString(), actualCount = count.IsEmpty ? null : count.ToString(), actualSection = section.IsEmpty ? null : section.ToString();
            Check(actualType == (string)definition["type_text"]!, $"type_text: expected '{definition["type_text"]}'; actual '{actualType ?? "<empty>"}'");
            Check(actualCount == (string)definition["core_count"]!, $"core_count: expected '{definition["core_count"]}'; actual '{actualCount ?? "<empty>"}'");
            Check(SameSection(actualSection, (double)definition["cross_section_mm2"]!), $"cross_section_mm2: expected '{definition["cross_section_mm2"]}'; actual '{actualSection ?? "<empty>"}'");
            foreach (Connection run in runs)
            {
                Check(selected.Add(run.ObjectIdentifier), FormattableString.Invariant($"duplicate core membership: actual {run.ObjectIdentifier}"));
                Check(run.ConnectionDefPoints.Length > 0, "definition points: actual 0");
                foreach (ConnectionDefinitionPoint point in run.ConnectionDefPoints)
                {
                    Check(point.CableDefinitionLine?.ObjectIdentifier == cable.ObjectIdentifier, $"core definition membership: actual '{point.CableDefinitionLine?.Name ?? "<none>"}'");
                    Check(CableDefinitionPlanner.SameName(cable.Name, point.Name, page), $"core definition designation: expected '{cable.Name}'; actual '{point.Name}'");
                    Check(point.VisibleName == cable.VisibleName, $"core definition visible name: expected '{cable.VisibleName}'; actual '{point.VisibleName}'");
                }
                using ConnectionPropertyList core = run.Properties;
                Check(CableDefinitionPlanner.SameName(cable.Name, CableText(core, 20005) ?? "", page), $"core membership: expected '{cable.Name}'; actual '{CableText(core, 20005) ?? "<empty>"}'");
                Check(SameSection(CableText(core, 31002), (double)definition["cross_section_mm2"]!), $"core cross_section_mm2: expected '{definition["cross_section_mm2"]}'; actual '{CableText(core, 31002) ?? "<empty>"}'");
                result.Add(new JObject { ["designation"] = cable.Name, ["type"] = type.ToString(), ["oid"] = run.ObjectIdentifier, ["pins"] = JArray.FromObject(CablePins(run)), ["label"] = CableText(core, 31011), ["colour"] = CableText(core, 31004) });
            }
        }
        JObject after = CableSnapshot(project, page), before = (JObject)plan["_cableBefore"]!.DeepClone();
        foreach (long id in selected)
        { after.Remove(id.ToString(CultureInfo.InvariantCulture)); before.Remove(id.ToString(CultureInfo.InvariantCulture)); }
        Check(JToken.DeepEquals(before, after), $"unselected connections: expected {before}; actual {after}");
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
    private static void CheckMembership(Cable cable, Connection[] runs)
    {
        long[] actual = cable.CableConnections.Select(c => c.ObjectIdentifier).OrderBy(id => id).ToArray(), expected = runs.Select(c => c.ObjectIdentifier).OrderBy(id => id).ToArray();
        Check(actual.SequenceEqual(expected), $"cable membership: expected {new JArray(expected)}; actual {new JArray(actual)}");
    }
    private static void Check(bool valid, string detail) { if (!valid) { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Cable readback differs: " + detail + "."); } }
}
