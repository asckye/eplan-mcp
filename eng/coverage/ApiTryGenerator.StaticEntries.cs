using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage;

internal static partial class ApiTryGenerator
{
    internal sealed record EntryPath(string Root, string Type, string Path, string Expression, bool Key = false);
    private sealed record EntryModel(ApiIndexData Data, Dictionary<string, string[]> Kinds, Dictionary<string, string[]> Enums, EntryPath[] Paths, Dictionary<string, JsonElement> Types);
    private static bool MatchEntry(string value, string pattern) => Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static bool EntryNamespace(string ns) => ns.StartsWith("Eplan.EplApi.DataModel.", StringComparison.Ordinal) || ns is "Eplan.EplApi.DataModel" or "Eplan.EplApi.HEServices" or "Eplan.EplApi.EServices" or "Eplan.EplApi.EServices.Ged" or "Eplan.EplApi.Base";
    private static bool SettingType(string type) => MatchEntry(type, "Settings|Scheme|ProjectSections");
    private static bool EntryRead(ApiEntry e) => e.Row.Kind == "property" || ReadMethod(e) || e.Name is "ArePlacedSymbolAlwaysTreatedAsGraphics" or "SupportsPageAutoconnecting"
        || SettingType(e.Type) && (e.Name.StartsWith("Get", StringComparison.Ordinal) || e.Name == "ExistSetting") && !e.Details.Where(d => d.Kind == "overload").SelectMany(d => d.Parameters!).Where(p => p.Name != "strSettingPath").Any(ApiTryServices.SideEffectParameter);
    internal static bool PublicEntryType(string type, IReadOnlyDictionary<string, JsonElement> types) => types.TryGetValue(type, out JsonElement t)
        && !t.TryGetProperty("excluded", out _) && !t.TryGetProperty("access", out _)
        && (!t.TryGetProperty("outer", out JsonElement outer) || PublicEntryType(outer.GetString()![2..], types));
    private static EntryModel EntryData(string root)
    {
        ApiIndexData data = Data(root);
        Dictionary<string, string[]> kinds = ObjectKindGenerator.Kinds(root);
        string typesFile = Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl");
        // Test repositories hold no catalogue, like Enums and ObjectKindGenerator.Load expect.
        var types = (File.Exists(typesFile) ? File.ReadLines(typesFile) : []).Select(l => JsonSerializer.Deserialize<JsonElement>(l)).ToDictionary(t => t.GetProperty("id").GetString()![2..], StringComparer.Ordinal);
        return new(data, kinds, Enums(root), EntryPaths(data, kinds, types), types);
    }
    internal static EntryPath[] EntryPaths(ApiIndexData data, IReadOnlyDictionary<string, string[]> kinds, IReadOnlyDictionary<string, JsonElement> types)
    {
        var result = new List<EntryPath>();
        bool SafeType(string type) => PublicEntryType(type, types) && types[type].GetProperty("kind").GetString() is "class" or "interface" && type.StartsWith("Eplan.EplApi.DataModel.", StringComparison.Ordinal)
            && !MatchEntry(type, "PropertyList|PropertyValue|Filter|ArticleReference|PropertyPlacement|MateEntry|MasterData\\.(?!PlotFrame$)");
        var properties = data.Entries.Where(e => e.Row.Kind == "property" && e.Row.ExcludedReason is null).SelectMany(e => e.Details.Where(d => d.Kind == "overload" && d.ExcludedReason is null && d.Access is null && !d.Static && d.Accessors?.Split(';').Contains("get", StringComparer.Ordinal) == true && SafeType(d.ReturnType ?? "")).Select(d => (e, d))).ToArray();
        void Follow(string parent, string origin, string path, string expression, int depth)
        {
            if (depth == 2)
            { return; }
            foreach ((ApiEntry e, ApiDetail d) in properties.Where(p => p.e.Type[2..] == parent))
            {
                bool key = e.Name == "Layer" && parent.EndsWith(".GraphicalLayerTable", StringComparison.Ordinal) && d.Parameters is [{ Type: "System.String" }];
                if (d.Parameters is not { Length: 0 } && !key)
                { continue; }
                string next = path.Length == 0 ? e.Name : path + "." + e.Name;
                string code = "Hold(" + expression + (key ? "[parameters[\"via_key\"]!.Value<string>()!]" : "." + e.Name) + ", own)";
                result.Add(new(origin, d.ReturnType!, next, code, key));
                if (!key)
                { Follow(d.ReturnType!, origin, next, code, depth + 1); }
            }
        }
        foreach (string type in kinds.Keys.Select(k => "Eplan.EplApi.DataModel." + k).Where(t => PublicEntryType(t, types)))
        { Follow(type, type, "", "((global::" + type + ")target)", 0); }
        return [.. result.DistinctBy(p => (p.Root, p.Type, p.Path))];
    }
    internal static string? EntryExclusion(ApiEntry e, ApiDetail d, IReadOnlyDictionary<string, string[]> enums, IReadOnlyDictionary<string, string[]> kinds)
    {
        string type = e.Type[2..];
        if (e.Row.ExcludedReason is not null || d.ExcludedReason is not null)
        { return "catalog:" + (e.Row.ExcludedReason ?? d.ExcludedReason); }
        if (d.Access is not null)
        { return "static_or_nonpublic"; }
        if (SettingType(type) && (!EntryRead(e) || e.Row.Kind == "property" && d.Accessors == "set"))
        { return "settings_write"; }
        if (MatchEntry(type, "SafetyPoint|Transaction|UndoStep|LockingStep|Disable.*Step|ProgressFactory|Context$|ProjectManager|License|Application"))
        { return "application_or_session_state"; }
        if (MatchEntry(type + "." + e.Name, "Network|Socket|Http|Ftp|Remote|WebService"))
        { return "external_or_application_effect"; }
        if (MatchEntry(e.Name, "FromStringIdentifier|TryParseIdentifier"))
        { return "unscoped_object_identifier"; }
        if (_excludedPrefixes.Any(p => e.Name.StartsWith(p, StringComparison.Ordinal)) || e.Name == "Dispose")
        { return "lifecycle_or_creation"; }
        bool setting = SettingType(type);
        ApiEntry checkedEntry = setting ? e with { Type = "T:Eplan.EplApi.DataModel.ProjectScoped", Name = EntryRead(e) ? "GetValue" : e.Name } : e;
        ApiDetail checkedMember = setting ? d with { Parameters = [.. d.Parameters!.Where(p => p.Name != "strSettingPath")] } : d;
        if (ApiTryServices.Exclusion(checkedEntry, checkedMember) is { } reason)
        { return reason; }
        if (MatchEntry(e.Name, "Export|Import|Save|Load|WriteSettings|ReadSettings|Clear.*Cache|Register|Unregister|Open|Close|Connect"))
        { return "external_or_application_effect"; }
        if (d.Static && !EntryRead(e) && !d.Parameters!.Any(p => ObjectType(p.Type, kinds)))
        { return "static_not_project_scoped"; }
        if (e.Row.Kind == "property")
        { return Scalar(d.ReturnType, enums) && d.Parameters is { Length: 0 } && d.Accessors?.Contains("get", StringComparison.Ordinal) == true ? null : "unsupported_property_shape"; }
        return Exclusion(e, d with { Static = false }, enums, kinds);
    }
    private static IEnumerable<(ApiEntry Entry, ApiDetail Member, string? Reason)> EntryMembers(EntryModel m) => m.Data.Entries.Where(e => EntryNamespace(e.Namespace) && e.Row.Kind is "method" or "property")
        .SelectMany(e => e.Details.Where(d => d.Kind == "overload" && (d.Static || !ObjectType(e.Type[2..], m.Kinds) && m.Paths.Any(p => p.Type == e.Type[2..])))
            .Select(d => (e, d, !PublicEntryType(e.Type[2..], m.Types) ? "entry_type_excluded" : EntryExclusion(e, d, m.Enums, m.Kinds))));
    private static (ApiEntry Entry, ApiDetail Member)[] EntrySelected(EntryModel m) => [.. EntryMembers(m).Where(s => s.Reason is null)
        .GroupBy(s => (s.Entry.Row.Group, Names(s.Member))).Where(g => g.Count() == 1).Select(g => (g.Single().Entry, g.Single().Member))];
    internal static string Catalog(string root)
    {
        string code = BaseCatalog(root);
        EntryModel m = EntryData(root);
        var selected = EntrySelected(m);
        string Switch(string head, IEnumerable<string> lines, string fallback = "null") => "    public static " + head + " switch { " + string.Join(" ", lines) + " _ => " + fallback + " };\n";
        string Shape(ApiEntry e, ApiDetail d) => "[" + string.Join(", ", CallShape(e, d).Select(Literal)) + "]";
        string Value(string type) => CollectionShape(type, m.Enums);
        return code + "public static partial class ApiTryCatalog\n{\n"
            + Switch("string[]? EntryShape(string group) => group", selected.Where(s => s.Entry.Row.Kind == "property").Select(s => Literal(s.Entry.Row.Group) + " => " + Shape(s.Entry, s.Member) + ","))
            + Switch("string[][] EntryMethods(string group) => group", selected.Where(s => s.Entry.Row.Kind == "method").GroupBy(s => s.Entry.Row.Group).Select(g => Literal(g.Key) + " => [" + string.Join(", ", g.Select(s => Shape(s.Entry, s.Member))) + "],"), "[]")
            + Switch("string[]? EntryReceiverShape(string signature) => signature", selected.Where(s => !s.Member.Static).Select(s => Literal(s.Member.Signature!) + " => [\"via\", " + Literal(s.Entry.Type[2..]) + "],"))
            + Switch("string[][] ViaPaths(string type) => type", m.Paths.GroupBy(p => p.Type).Select(g => Literal(g.Key) + " => [" + string.Join(", ", g.Select(p => "[" + Literal(p.Root) + ", " + Literal(p.Path) + "]")) + "],"), "[]")
            + Switch("string[]? EntryMethodShape(string group, System.Collections.Generic.IEnumerable<string> names) => (group, string.Join(\"|\", System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(names, n => n != \"target\" && n != \"via\" && n != \"via_key\"), n => n, System.StringComparer.Ordinal)))", selected.Where(s => s.Entry.Row.Kind == "method").Select(s => "(" + Literal(s.Entry.Row.Group) + ", " + Literal(Names(s.Member)) + ") => " + Shape(s.Entry, s.Member) + ","))
            + Switch("string? EntryAccess(string group) => group", selected.Where(s => s.Entry.Row.Kind == "method").DistinctBy(s => s.Entry.Row.Group).Select(s => Literal(s.Entry.Row.Group) + " => " + Literal(EntryRead(s.Entry) ? "read" : "write") + ","))
            + Switch("string[]? EntryParameter(string signature, string name) => (signature, name)", selected.SelectMany(s => Inputs(s.Member).Select(p => "(" + Literal(s.Member.Signature!) + ", " + Literal(ParameterName(p)) + ") => " + Value(p.Type) + ",")))
            + Switch("string[]? EntryValue(string group) => group", selected.Where(s => EntryWritable(s.Entry, s.Member)).Select(s => Literal(s.Entry.Row.Group) + " => " + Value(s.Member.ReturnType!) + ","))
            + Switch("bool Entry(string signature) => signature", selected.Select(s => Literal(s.Member.Signature!) + " => true,"), "false")
            + Switch("bool Static(string signature) => signature", selected.Where(s => s.Member.Static).Select(s => Literal(s.Member.Signature!) + " => true,"), "false")
            + Switch("bool ViaKey(string type, string via) => (type, via)", m.Paths.Where(p => p.Key).DistinctBy(p => (p.Type, p.Path)).Select(p => "(" + Literal(p.Type) + ", " + Literal(p.Path) + ") => true,"), "false")
            + Switch("string[] ViaKinds(string type, string via) => (type, via)", m.Paths.GroupBy(p => (p.Type, p.Path)).Select(g => "(" + Literal(g.Key.Type) + ", " + Literal(g.Key.Path) + ") => [" + string.Join(", ", g.SelectMany(p => m.Kinds[p.Root["Eplan.EplApi.DataModel.".Length..]]).Distinct(StringComparer.Ordinal).Select(Literal)) + "],"), "[]") + "}\n";
    }
    internal static bool EntryWritable(ApiEntry e, ApiDetail d) => e.Row.Kind == "property" && !d.Static && !SettingType(e.Type) && !e.Type.Contains(".MasterData.", StringComparison.Ordinal) && Writable(d) && d.ReturnType != "System.Int64";
    internal static string Dispatch(string root)
    {
        EntryModel m = EntryData(root);
        var selected = EntrySelected(m);
        string ConvertValue(string type, string token) => m.Enums.TryGetValue(type, out string[]? values) ? "(" + token + ".Value<string>() switch { " + string.Join(", ", values.Select(v => Literal(v) + " => global::" + type + "." + v)) + ", _ => throw new System.InvalidOperationException(\"Invalid enum.\") })" : token + ".Value<" + type + ">()";
        return BaseDispatch(root) + "internal static class ApiTryEntryDispatch\n{\n    internal static object? Invoke(string signature, object? target, JObject parameters, System.Func<JToken, string, object> resolve)\n    {\n        switch (signature)\n        {\n"
            + string.Join("\n", selected.Select(s =>
            {
                string receiver = s.Member.Static ? "global::" + s.Entry.Type[2..] : "((global::" + s.Entry.Type[2..] + ")target!)";
                if (s.Entry.Row.Kind == "method")
                { return MethodCase(s.Entry, s.Member, m.Enums, m.Kinds, new Dictionary<string, ApiTryServices.Service>(), receiver); }
                string call = receiver + "." + s.Entry.Name;
                return "            case " + Literal(s.Member.Signature!) + ": { " + (EntryWritable(s.Entry, s.Member) ? "if (parameters.ContainsKey(\"value\")) { " + call + " = " + ConvertValue(s.Member.ReturnType!, "parameters[\"value\"]!") + "; } " : "")
                    + (s.Member.ReturnType == "System.Void" ? "" : "return ") + call + ";" + (s.Member.ReturnType == "System.Void" ? " return null;" : "") + " }";
            })) + "\n            default: throw new System.InvalidOperationException(\"No entry dispatch.\");\n        }\n    }\n    private static T Hold<T>(T value, System.Action<System.IDisposable> own) { if (value is System.IDisposable disposable && value is not StorableObject) { own(disposable); } return value; }\n    internal static object? Via(string type, string path, StorableObject target, JObject parameters, System.Action<System.IDisposable> own) => (type, path) switch\n    {\n"
            + string.Join("\n", m.Paths.GroupBy(p => (p.Type, p.Path)).Select(g => "        (" + Literal(g.Key.Type) + ", " + Literal(g.Key.Path) + ") => " + string.Join(" : ", g.Select(p => "target is global::" + p.Root + " ? (object?)" + p.Expression)) + " : null,"))
            + "\n        _ => null,\n    };\n}\n";
    }
    private static string EntryReport(string root, string original)
    {
        EntryModel m = EntryData(root);
        var selected = EntrySelected(m).Select(s => s.Member.Id).ToHashSet(StringComparer.Ordinal);
        var additions = EntryMembers(m).Where(s => s.Entry.Row.Kind == "method").Select(s => s.Entry.Row.Group + "\t" + s.Member.Id + "\t" + (s.Reason is { } reason ? "excluded\t" + reason : !selected.Contains(s.Member.Id) ? "excluded\tambiguous_parameter_names" : (EntryRead(s.Entry) ? "read" : "write") + "\t" + (s.Member.Static ? "public_static" : "property_entry"))).ToArray();
        var ids = additions.Select(l => l.Split('\t')[1]).ToHashSet(StringComparer.Ordinal);
        return string.Join("\n", original.TrimEnd('\n').Split('\n').Where(l => !ids.Contains(l.Split('\t')[1])).Concat(additions).OrderBy(l => l == "group\tsignature\tstatus\treason" ? "" : l, StringComparer.Ordinal)) + "\n";
    }
    internal static IEnumerable<string> ObjectEntrySources(string type, ApiIndexData data)
    {
        bool Has(string id) => data.Entries.Any(e => e.Row.ExcludedReason is null && e.Details.Any(d => d.Id == id && d.ExcludedReason is null && d.Access is null && !d.Static));
        return type switch
        {
            "Eplan.EplApi.DataModel.Location" when Has("M:Eplan.EplApi.DataModel.Project.GetLocationObjects") => ["project.GetLocationObjects()"],
            "Eplan.EplApi.DataModel.MasterData.PlotFrame" when Has("P:Eplan.EplApi.DataModel.Page.PlotFrame") => ["finder.GetPages(null).Select(p => p.PlotFrame).Where(p => p != null)"],
            _ => [],
        };
    }
}
