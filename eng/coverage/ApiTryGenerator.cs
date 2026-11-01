using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage;
/// <summary>Catalog-derived closed dispatch. Extend the reviewed type/member selection, never runtime reflection.</summary>
internal static partial class ApiTryGenerator
{
    internal const string CatalogPath = "src/EplanMcp.Contracts/Generated/ApiTryCatalog.g.cs";
    internal const string DispatchPath = "src/EplanMcp.Bridge.Eplan/Adapters/ApiTryDispatch.g.cs";
    internal const string ReportPath = "catalog/api-try.g.tsv";
    private static ApiIndexData Data(string root)
    {
        string path = Path.Combine(root, "src/EplanMcp.Bridge.Eplan/BannedSymbols.txt");
        var banned = (File.Exists(path) ? File.ReadLines(path).Where(l => !l.StartsWith("//", StringComparison.Ordinal) && l.Contains(';', StringComparison.Ordinal)).Select(l => l.Split(';')[0]) : []).ToHashSet(StringComparer.Ordinal);
        ApiIndexData data = ApiIndexGenerator.Data(root);
        return data with { Entries = [.. data.Entries.Select(e => e with { Details = [.. e.Details.Select(d => banned.Contains(d.Id) || banned.Contains(e.Type) || BannedFactory(e, d, banned) ? d with { ExcludedReason = "banned_symbol" } : d)] })] };
    }
    private static string Literal(string value) => JsonSerializer.Serialize(value);
    private static readonly string[] _enumShape = ["enum"];
    private static readonly string[] _readPrefixes = ["Get", "Is", "Has", "Can", "Find"];
    // Reviewed lifecycle calls use the closed lifecycle policy; session state remains excluded.
    private static readonly string[] _excludedPrefixes = ["Remove", "Delete", "Erase", "Create", "Destroy", "Init", "init", "Freeze", "Lock", "#"];
    private static Dictionary<string, string[]> Enums(string root) => (File.Exists(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl")) ? File.ReadLines(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl")) : [])
        .Select(l => JsonSerializer.Deserialize<JsonElement>(l)).Where(t => t.GetProperty("kind").GetString() == "enum" && t.TryGetProperty("values", out _) && !t.TryGetProperty("excluded", out _))
        .ToDictionary(t => t.GetProperty("id").GetString()![2..], t => t.GetProperty("values").EnumerateObject().Select(p => p.Name).ToArray(), StringComparer.Ordinal);
    internal static bool Writable(ApiDetail member) => member.Id != "P:Eplan.EplApi.DataModel.ViewPart.UseLocalPropertyPlacements" && member.SetterAccess is null && member.Access is null && member.Accessors?.Split(';').Contains("set", StringComparer.Ordinal) == true;
    private static bool ObjectType(string? type, IReadOnlyDictionary<string, string[]>? kinds = null) => kinds is null ? ApiTryCatalog.Kinds(type).Length > 0 : kinds.ContainsKey((type ?? "").Replace("Eplan.EplApi.DataModel.", "", StringComparison.Ordinal));
    private static bool Scalar(string? type, IReadOnlyDictionary<string, string[]>? enums) => type is "System.String" or "System.Boolean" or "System.Double" or "System.Int32" or "System.Int64" || type is not null && enums?.ContainsKey(type) == true;
    internal static bool ReadMethod(ApiEntry entry) => _readPrefixes.Any(p => entry.Name.StartsWith(p, StringComparison.Ordinal))
        && !entry.Details.Where(d => d.Kind == "overload").SelectMany(d => d.Parameters!).Any(ApiTryServices.SideEffectParameter) && !FileOutput(entry);
    private static string ParameterName(ApiParameter p) => p.Name == "target" ? "argument_target" : p.Name;
    internal static string[] CallShape(ApiEntry entry, ApiDetail member) => [entry.Type[2..], member.Signature!, .. Inputs(member).Select(ParameterName)];
    internal static string Names(ApiDetail member) => string.Join("|", Inputs(member).Select(ParameterName).Order(StringComparer.Ordinal));
    private static string Classification(ApiEntry entry, bool service) => entry.Row.Kind == "property" ? "read\tgetter" : (ReadMethod(entry) ? "read" : "write") + "\t"
        + (service ? ReadMethod(entry) ? "read_prefix_without_side_effect_parameters" : _readPrefixes.Any(p => entry.Name.StartsWith(p, StringComparison.Ordinal)) ? "side_effect_parameters" : "service_method_write" : "-");
    internal static string? Exclusion(ApiEntry entry, ApiDetail member, IReadOnlyDictionary<string, string[]>? enums, IReadOnlyDictionary<string, string[]>? kinds = null, IReadOnlyDictionary<string, ApiTryServices.Service>? services = null)
    {
        if (entry.Row.ExcludedReason is not null || member.ExcludedReason is not null)
        { return "catalog:" + (entry.Row.ExcludedReason ?? member.ExcludedReason); }
        bool service = services?.ContainsKey(entry.Type[2..]) == true;
        if (service && (services![entry.Type[2..]].Reason ?? FileExclusion(entry, member)) is { } reason)
        { return reason; }
        if (LifecycleExclusion(entry, member) is { } lifecycleReason)
        { return lifecycleReason; }
        if (entry.Name == "Dispose" || entry.Name.StartsWith('#') && !Factory(entry) || !service && _excludedPrefixes.Any(p => entry.Name.StartsWith(p, StringComparison.Ordinal)) && !Lifecycle(entry))
        { return "lifecycle_or_creation"; }
        if (member.Static || member.Access is not null)
        { return "static_or_nonpublic"; }
        return ShapeExclusion(member, enums, kinds);
    }
    internal static IEnumerable<(ApiEntry Entry, ApiDetail Member)> Methods(ApiIndexData data, IReadOnlyDictionary<string, string[]>? enums = null, IReadOnlyDictionary<string, string[]>? kinds = null, IReadOnlyDictionary<string, ApiTryServices.Service>? services = null) => data.Entries
        .Where(e => e.Row.Kind == "method" && (ObjectType(e.Type[2..], kinds) || services?.ContainsKey(e.Type[2..]) == true)).SelectMany(e => e.Details.Where(d => d.Kind == "overload" && Exclusion(e, d, enums, kinds, services) is null)
            .GroupBy(Names, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => (e, g.Single())));
    internal static IEnumerable<(ApiEntry Entry, ApiDetail Member)> Selected(ApiIndexData data, IReadOnlyDictionary<string, string[]>? enums = null, IReadOnlyDictionary<string, string[]>? kinds = null) => data.Entries
        .Where(e => e.Row.ExcludedReason is null && ObjectType(e.Type[2..], kinds))
        .SelectMany(e => e.Details.Where(d => d.Kind == "overload" && d.ExcludedReason is null && d.Access is null && !d.Static
            && PropertyReturn(d.ReturnType, enums, kinds)
            && (e.Row.Kind == "property" && (d.Accessors?.Split(';').Contains("get", StringComparer.Ordinal) == true
                    || d.Id == "P:Eplan.EplApi.DataModel.EObjects.TerminalStrip.FunctionCategory") && d.Parameters is { Length: 0 }
                ))
            .Select(d => (e, d)));
    internal static string Report(string root)
    {
        ApiIndexData data = Data(root);
        Dictionary<string, string[]> kinds = ObjectKindGenerator.Kinds(root);
        Dictionary<string, string[]> enums = Enums(root);
        Dictionary<string, ApiTryServices.Service> services = ApiTryServices.Load(root);
        return SettingsReport(data, HandleReport(root, EntryReport(root, "group\tsignature\tstatus\treason\n" + string.Join("\n", data.Entries.Where(e => e.Row.Kind == "method" && (ObjectType(e.Type[2..], kinds) || services.ContainsKey(e.Type[2..])))
            .SelectMany(e => e.Details.Where(d => d.Kind == "overload").Select(d => e.Row.Group + "\t" + d.Id + "\t"
                + (Exclusion(e, d, enums, kinds, services) is { } reason ? "excluded\t" + reason : e.Details.Count(o => o.Kind == "overload" && Names(o) == Names(d) && Exclusion(e, o, enums, kinds, services) is null) != 1 ? "excluded\tambiguous_parameter_names" : Classification(e, services.ContainsKey(e.Type[2..])))))) + "\n")));
    }
    private static string BaseCatalog(string root)
    {
        ApiIndexData data = Data(root);
        Dictionary<string, string[]> kinds = ObjectKindGenerator.Kinds(root);
        Dictionary<string, string[]> enums = Enums(root);
        Dictionary<string, ApiTryServices.Service> services = ApiTryServices.Load(root);
        (ApiEntry Entry, ApiDetail Member)[] selected = [.. Selected(data, enums, kinds)];
        (ApiEntry Entry, ApiDetail Member)[] methods = [.. Methods(data, enums, kinds, services)];
        string Shape(string type) => CollectionShape(type, enums);
        (ApiEntry Entry, ApiDetail Member)[] entries = EntrySelected(EntryData(root));
        HandleModel handleModel = Handles(root);
        (ApiEntry Entry, ApiDetail Member)[] handles = handleModel.Members;
        HashSet<string> dispatchable = [.. selected.Concat(methods).Concat(entries).Concat(handles).Concat(SettingsMembers(data)).Select(s => s.Entry.Row.Group)];
        string Reason(ApiEntry entry) => entry.Row.ExcludedReason?.Split(':')[0] ?? (entry.Row.Kind != "method" ? "unsupported_property_or_receiver"
            : !ObjectType(entry.Type[2..], kinds) && !services.ContainsKey(entry.Type[2..]) ? "unsupported_receiver"
            : string.Join("; ", entry.Details.Where(d => d.Kind == "overload").Select(d => (Exclusion(entry, d, enums, kinds, services) ?? "ambiguous_parameter_names").Split(": ", StringSplitOptions.None)[0]).Distinct()));
        return "// <auto-generated/>\n#nullable enable\nnamespace EplanMcp.Contracts;\npublic static partial class ApiTryCatalog\n{\n"
            + "    public static bool Allowed(string group) => group switch\n    {\n"
            + string.Join("\n", data.Entries.Where(e => e.Row.ExcludedReason is null && e.Row.Kind is "method" or "property" && !SettingsBlocked(e)).Select(e => "        " + Literal(e.Row.Group) + " => true,"))
            + "\n        _ => false,\n    };\n    public static string[]? Shape(string group) => group switch\n    {\n"
            + string.Join("\n", selected.Select(s => "        " + Literal(s.Entry.Row.Group) + " => [" + string.Join(", ", new[] { s.Entry.Type[2..], s.Member.Signature! }.Concat(s.Member.Parameters!.Select(p => p.Name)).Select(Literal)) + "],"))
            + "\n        _ => EntryShape(group) ?? HandleShape(group),\n    };\n    public static string[]? Shape(string group, System.Collections.Generic.IEnumerable<string> names) => Settings(group) && !System.Linq.Enumerable.Contains(names, \"via\") ? SettingsShape(group, names) ?? EntryMethodShape(group, names) ?? HandleMethodShape(group, names) : Shape(group) ?? ((group, string.Join(\"|\", System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(names, n => n != \"target\" && n != \"via\" && n != \"via_key\"), n => n, System.StringComparer.Ordinal))) switch\n    {\n"
            + string.Join("\n", methods.Select(s => "        (" + Literal(s.Entry.Row.Group) + ", " + Literal(Names(s.Member)) + ") => [" + string.Join(", ", CallShape(s.Entry, s.Member).Select(Literal)) + "],"))
            + "\n        _ => EntryMethodShape(group, names) ?? HandleMethodShape(group, names),\n    });\n    public static string[][] Methods(string group) => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(SettingsMethods(group), group switch\n    {\n"
            + string.Join("\n", methods.Concat(entries.Where(s => s.Entry.Row.Kind == "method")).Concat(handles.Where(s => s.Entry.Row.Kind == "method")).DistinctBy(s => s.Member.Signature).GroupBy(s => s.Entry.Row.Group).Select(g => "        " + Literal(g.Key) + " => [" + string.Join(", ", g.Select(s => "[" + string.Join(", ", CallShape(s.Entry, s.Member).Select(Literal)) + "]")) + "],"))
            + "\n        _ => EntryMethods(group),\n    }));\n    public static string[] PropertyGroups => [" + string.Join(", ", selected.Concat(entries.Where(s => s.Entry.Row.Kind == "property")).Concat(handles.Where(s => s.Entry.Row.Kind == "property")).Select(s => s.Entry.Row.Group).Distinct(StringComparer.Ordinal).Select(Literal)) + "];\n    public static string? ExclusionReason(string group) => group switch\n    {\n"
            + string.Join("\n", data.Entries.Where(e => !dispatchable.Contains(e.Row.Group)).Select(e => "        " + Literal(e.Row.Group) + " => " + Literal(Reason(e)) + ","))
            + "\n        _ => null,\n    };\n    public static string? MethodAccess(string group) => Settings(group) ? \"read\" : group switch\n    {\n"
            + string.Join("\n", methods.DistinctBy(s => s.Entry.Row.Group).Select(s => "        " + Literal(s.Entry.Row.Group) + " => " + Literal(ReadMethod(s.Entry) ? "read" : "write") + ","))
            + "\n        _ => EntryAccess(group) ?? HandleMethodAccess(group),\n    };\n    public static string[] Producers(string type) => type switch\n    {\n"
            + string.Join("\n", methods.Concat(selected).Concat(entries).Concat(handles).SelectMany(s => ProducedTypes(s.Member).Select(type => (s.Entry, Type: type))).GroupBy(s => s.Type).Select(g => "        " + Literal(g.Key) + " => [" + string.Join(", ", g.Select(s => Literal(s.Entry.Row.Group)).Distinct()) + "],"))
            + "\n        _ => [],\n    };\n    public static string[]? ReceiverShape(string signature) => signature switch\n    {\n"
            + string.Join("\n", methods.Concat(selected).Concat(handles).DistinctBy(s => s.Member.Signature).Where(s => !s.Member.Static && (!s.Entry.Name.StartsWith('#') || Factory(s.Entry))).Select(s => "        " + Literal(s.Member.Signature!) + " => " + (Factory(s.Entry) || services.ContainsKey(s.Entry.Type[2..]) && (methods.Any(m => m.Member.Id == s.Member.Id) || !handleModel.Reachable.Contains(s.Entry.Type[2..])) ? "[\"Eplan.EplApi.DataModel.Project\"]" : ObjectType(s.Entry.Type[2..], kinds) ? Shape(s.Entry.Type[2..]) : "[\"handle\", " + Literal(s.Entry.Type[2..]) + "]") + ","))
            + "\n        _ => EntryReceiverShape(signature) ?? SettingsReceiverShape(signature),\n    };\n    public static string[]? ParameterShape(string signature, string name) => SettingsParameterShape(signature, name) ?? ((signature, name) switch\n    {\n"
            + string.Join("\n", methods.SelectMany(s => Inputs(s.Member).Select(p => "        (" + Literal(s.Member.Signature!) + ", " + Literal(ParameterName(p)) + ") => " + Shape(p.Type) + ",")))
            + "\n        _ => EntryParameter(signature, name) ?? HandleParameterShape(signature, name),\n    });\n" + FileCatalog(methods, services) + "    public static string[]? ValueShape(string group) => group switch\n    {\n"
            + string.Join("\n", selected.Where(s => PropertyWritable(s.Member, enums)).Select(s => "        " + Literal(s.Entry.Row.Group) + " => [" + string.Join(", ", (enums.TryGetValue(s.Member.ReturnType!, out string[]? values) ? _enumShape.Concat(values) : [s.Member.ReturnType!]).Select(Literal)) + "],"))
            + "\n        _ => EntryValue(group) ?? HandleValueShape(group),\n    };\n    public static bool Service(string group) => group switch { " + string.Join(" ", methods.Where(s => services.ContainsKey(s.Entry.Type[2..])).DistinctBy(s => s.Entry.Row.Group).Select(s => Literal(s.Entry.Row.Group) + " => true,")) + " _ => HandleService(group) };\n"
            + "    public static string[] TargetKinds(string group) => Settings(group) || Service(group)" + FactoryTargetCondition + " ? [\"project\"] : Kinds(Shape(group)?[0] ?? (group switch { " + string.Join(" ", methods.DistinctBy(s => s.Entry.Row.Group).Select(s => Literal(s.Entry.Row.Group) + " => " + Literal(s.Entry.Type[2..]) + ",")) + " _ => null }));\n    public static string[] Kinds(string? type) => type switch\n    {\n"
            + string.Join("\n", kinds.Select(p => "        " + Literal("Eplan.EplApi.DataModel." + p.Key) + " => [" + string.Join(", ", p.Value.Select(Literal)) + "],"))
            + "\n        _ => [],\n    };\n"
            + SettingsCatalog(data)
            + "    public static bool ValidValue(string[]? shape, string kind, string text) => shape?[0] switch\n    {\n"
            + "        \"System.Boolean\" => kind == \"Boolean\",\n        \"System.String\" => kind == \"String\",\n"
            + "        \"System.Int32\" => kind == \"Integer\" && int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),\n"
            + "        \"System.Int64\" => kind == \"Integer\" && long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),\n"
            + "        \"System.Double\" => (kind == \"Integer\" || kind == \"Float\") && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) && !double.IsNaN(n) && !double.IsInfinity(n),\n"
            + "        \"enum\" => kind == \"String\" && System.Linq.Enumerable.Contains(System.Linq.Enumerable.Skip(shape, 1), text, System.StringComparer.Ordinal),\n"
            + "        \"Eplan.EplApi.Base.PointD\" => kind == \"PointD\" || kind == \"handle\",\n        _ => shape is not null && (Kinds(shape[0]).Length > 0 && kind == \"locator\" || HandleType(shape[0]) && kind == \"handle\"),\n    };\n" + HandleCatalog(root) + "}\n";
    }
    private static string Target(ApiEntry entry) => "((global::" + entry.Type[2..] + ")target)." + entry.Name;
    private static string BaseDispatch(string root)
    {
        Dictionary<string, string[]> enums = Enums(root);
        ApiIndexData data = Data(root);
        Dictionary<string, string[]> kinds = ObjectKindGenerator.Kinds(root);
        Dictionary<string, ApiTryServices.Service> services = ApiTryServices.Load(root);
        (ApiEntry Entry, ApiDetail Member)[] selected = [.. Selected(data, enums, kinds)];
        return "// <auto-generated/>\n#nullable enable\nusing Eplan.EplApi.DataModel;\nusing Newtonsoft.Json.Linq;\nusing System.Linq;\nnamespace EplanMcp.Bridge.Eplan.Adapters;\ninternal static class ApiTryDispatch\n{\n"
        + "    internal static object? Read(string group, StorableObject target, JObject parameters) => group switch\n    {\n"
        + string.Join("\n", selected.Select(s => "        " + Literal(s.Entry.Row.Group) + " => (object)" + (s.Member.Accessors == "set" ? "((global::Eplan.EplApi.DataModel.Function)target).FunctionCategory" : Target(s.Entry))
            + (s.Entry.Row.Kind == "method" ? "(" + string.Join(", ", s.Member.Parameters!.Select(p => "parameters[" + Literal(p.Name) + "]!.Value<bool>()")) + ")" : "") + ","))
        + "\n        _ => throw new System.InvalidOperationException(\"No compiled dispatch for this group and shape.\"),\n    };\n"
        + "    internal static void Write(string group, StorableObject target, JObject parameters)\n    {\n        switch (group)\n        {\n"
        + string.Join("\n", selected.Where(s => PropertyWritable(s.Member, enums)).Select(s => "            case " + Literal(s.Entry.Row.Group) + ": " + Target(s.Entry) + " = "
            + (enums.TryGetValue(s.Member.ReturnType!, out string[]? values)
                ? "parameters[\"value\"]!.Value<string>() switch { " + string.Join(", ", values.Select(v => Literal(v) + " => global::" + s.Member.ReturnType + "." + v)) + ", _ => throw new System.InvalidOperationException(\"Invalid enum value.\") }"
                : "parameters[\"value\"]!.Value<" + s.Member.ReturnType + ">()") + "; break;"))
        + "\n            default: throw new System.InvalidOperationException(\"No compiled setter for this group.\");\n        }\n    }\n    internal static object? Invoke(string signature, StorableObject target, JObject parameters, System.Func<JToken, string, object> resolve)\n    {\n        if (SettingsSignature(signature)) { return InvokeSettings(signature, (Project)target, parameters); }\n        switch (signature)\n        {\n"
        + string.Join("\n", Methods(data, enums, kinds, services).Select(s => MethodCase(s.Entry, s.Member, enums, kinds, services)))
        + "\n            default: throw new System.InvalidOperationException(\"No compiled method for this signature.\");\n        }\n    }\n" + SettingsDispatch(data) + LifecycleReadback(data, kinds) + "}\n" + HandleDispatch(root);
    }
}
