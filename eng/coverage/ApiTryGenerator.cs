using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage;
/// <summary>Catalog-derived closed dispatch. Extend the reviewed type/member selection, never runtime reflection.</summary>
internal static class ApiTryGenerator
{
    internal const string CatalogPath = "src/EplanMcp.Contracts/Generated/ApiTryCatalog.g.cs";
    internal const string DispatchPath = "src/EplanMcp.Bridge.Eplan/Adapters/ApiTryDispatch.g.cs";
    internal const string ReportPath = "catalog/api-try.g.tsv";
    private static ApiIndexData Data(string root)
    {
        string path = Path.Combine(root, "src/EplanMcp.Bridge.Eplan/BannedSymbols.txt");
        var banned = (File.Exists(path) ? File.ReadLines(path).Where(l => !l.StartsWith("//", StringComparison.Ordinal) && l.Contains(';', StringComparison.Ordinal)).Select(l => l.Split(';')[0]) : []).ToHashSet(StringComparer.Ordinal);
        ApiIndexData data = ApiIndexGenerator.Data(root);
        return data with { Entries = [.. data.Entries.Select(e => e with { Details = [.. e.Details.Select(d => banned.Contains(d.Id) || banned.Contains(e.Type) ? d with { ExcludedReason = "banned_symbol" } : d)] })] };
    }
    private static string Literal(string value) => JsonSerializer.Serialize(value);
    private static readonly string[] _enumShape = ["enum"];
    private static readonly string[] _readPrefixes = ["Get", "Is", "Has", "Can", "Find"];
    private static readonly string[] _excludedPrefixes = ["Remove", "Delete", "Erase", "Create", "#"];
    private static Dictionary<string, string[]> Enums(string root) => (File.Exists(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl")) ? File.ReadLines(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl")) : [])
        .Select(l => JsonSerializer.Deserialize<JsonElement>(l)).Where(t => t.GetProperty("kind").GetString() == "enum" && t.TryGetProperty("values", out _) && !t.TryGetProperty("excluded", out _))
        .ToDictionary(t => t.GetProperty("id").GetString()![2..], t => t.GetProperty("values").EnumerateObject().Select(p => p.Name).ToArray(), StringComparer.Ordinal);
    internal static bool Writable(ApiDetail member) => member.Id != "P:Eplan.EplApi.DataModel.ViewPart.UseLocalPropertyPlacements" && member.SetterAccess is null && member.Access is null && member.Accessors?.Split(';').Contains("set", StringComparer.Ordinal) == true;
    private static bool ObjectType(string? type, IReadOnlyDictionary<string, string[]>? kinds = null) => kinds is null ? ApiTryCatalog.Kinds(type).Length > 0 : kinds.ContainsKey((type ?? "").Replace("Eplan.EplApi.DataModel.", "", StringComparison.Ordinal));
    private static bool Scalar(string? type, IReadOnlyDictionary<string, string[]>? enums) => type is "System.String" or "System.Boolean" or "System.Double" or "System.Int32" or "System.Int64" || type is not null && enums?.ContainsKey(type) == true;
    internal static bool ReadMethod(ApiEntry entry) => _readPrefixes.Any(p => entry.Name.StartsWith(p, StringComparison.Ordinal))
        && !entry.Details.Where(d => d.Kind == "overload").SelectMany(d => d.Parameters!).Any(ApiTryServices.SideEffectParameter);
    private static string ParameterName(ApiParameter p) => p.Name == "target" ? "argument_target" : p.Name;
    internal static string Names(ApiDetail member) => string.Join("|", member.Parameters!.Select(ParameterName).Order(StringComparer.Ordinal));
    private static string Classification(ApiEntry entry, bool service) => (ReadMethod(entry) ? "read" : "write") + "\t"
        + (service ? ReadMethod(entry) ? "read_prefix_without_side_effect_parameters" : _readPrefixes.Any(p => entry.Name.StartsWith(p, StringComparison.Ordinal)) ? "side_effect_parameters" : "service_method_write" : "-");
    internal static string? Exclusion(ApiEntry entry, ApiDetail member, IReadOnlyDictionary<string, string[]>? enums, IReadOnlyDictionary<string, string[]>? kinds = null, IReadOnlyDictionary<string, ApiTryServices.Service>? services = null)
    {
        if (entry.Row.ExcludedReason is not null || member.ExcludedReason is not null)
        { return "catalog:" + (entry.Row.ExcludedReason ?? member.ExcludedReason); }
        bool service = services?.ContainsKey(entry.Type[2..]) == true;
        if (service && (services![entry.Type[2..]].Reason ?? ApiTryServices.Exclusion(entry, member)) is { } reason)
        { return reason; }
        if (entry.Name == "Dispose" || entry.Name.StartsWith('#') || !service && _excludedPrefixes.Any(p => entry.Name.StartsWith(p, StringComparison.Ordinal)))
        { return "lifecycle_or_creation"; }
        if (member.Static || member.Access is not null)
        { return "static_or_nonpublic"; }
        if (member.Parameters!.Any(p => p.Type.Contains('@', StringComparison.Ordinal)))
        { return "ref_out"; }
        if (member.Parameters!.Any(p => p.Type.Contains('[', StringComparison.Ordinal)))
        { return "array_or_params"; }
        if (member.Parameters!.Any(p => p.Type.StartsWith("System.Action", StringComparison.Ordinal) || p.Type.StartsWith("System.Func", StringComparison.Ordinal)))
        { return "delegate_parameter"; }
        if (member.Id.Contains('`', StringComparison.Ordinal) || member.Parameters!.Any(p => p.Type.Contains('{', StringComparison.Ordinal) || p.Type.Contains('`', StringComparison.Ordinal) || p.Type.StartsWith("System.Collections.", StringComparison.Ordinal)))
        { return "generic_or_collection_parameter"; }
        if (member.Parameters!.Any(p => !Scalar(p.Type, enums) && p.Type != "Eplan.EplApi.Base.PointD" && !ObjectType(p.Type, kinds)))
        { return "unsupported_parameter"; }
        return member.ReturnType != "System.Void" && !Scalar(member.ReturnType, enums) && !ObjectType(member.ReturnType, kinds) && member.ReturnType != "Eplan.EplApi.DataModel.StorableObject" ? "unsupported_return" : null;
    }
    internal static IEnumerable<(ApiEntry Entry, ApiDetail Member)> Methods(ApiIndexData data, IReadOnlyDictionary<string, string[]>? enums = null, IReadOnlyDictionary<string, string[]>? kinds = null, IReadOnlyDictionary<string, ApiTryServices.Service>? services = null) => data.Entries
        .Where(e => e.Row.Kind == "method" && (ObjectType(e.Type[2..], kinds) || services?.ContainsKey(e.Type[2..]) == true)).SelectMany(e => e.Details.Where(d => d.Kind == "overload" && Exclusion(e, d, enums, kinds, services) is null)
            .GroupBy(Names, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => (e, g.Single())));
    internal static IEnumerable<(ApiEntry Entry, ApiDetail Member)> Selected(ApiIndexData data, IReadOnlyDictionary<string, string[]>? enums = null, IReadOnlyDictionary<string, string[]>? kinds = null) => data.Entries
        .Where(e => e.Row.ExcludedReason is null && ObjectType(e.Type[2..], kinds))
        .SelectMany(e => e.Details.Where(d => d.Kind == "overload" && d.ExcludedReason is null && d.Access is null && !d.Static
            && (d.ReturnType is "System.String" or "System.Boolean" or "System.Double" or "System.Int32" or "System.Int64" || enums?.ContainsKey(d.ReturnType!) == true)
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
        return "group\tsignature\tstatus\treason\n" + string.Join("\n", data.Entries.Where(e => e.Row.Kind == "method" && (ObjectType(e.Type[2..], kinds) || services.ContainsKey(e.Type[2..])))
            .SelectMany(e => e.Details.Where(d => d.Kind == "overload").Select(d => e.Row.Group + "\t" + d.Id + "\t"
                + (Exclusion(e, d, enums, kinds, services) is { } reason ? "excluded\t" + reason : e.Details.Count(o => o.Kind == "overload" && Names(o) == Names(d) && Exclusion(e, o, enums, kinds, services) is null) != 1 ? "excluded\tambiguous_parameter_names" : Classification(e, services.ContainsKey(e.Type[2..])))))) + "\n";
    }
    internal static string Catalog(string root)
    {
        ApiIndexData data = Data(root);
        Dictionary<string, string[]> kinds = ObjectKindGenerator.Kinds(root);
        Dictionary<string, string[]> enums = Enums(root);
        Dictionary<string, ApiTryServices.Service> services = ApiTryServices.Load(root);
        (ApiEntry Entry, ApiDetail Member)[] selected = [.. Selected(data, enums, kinds)];
        (ApiEntry Entry, ApiDetail Member)[] methods = [.. Methods(data, enums, kinds, services)];
        string Shape(string type) => "[" + string.Join(", ", (enums.TryGetValue(type, out string[]? values) ? _enumShape.Concat(values) : [type]).Select(Literal)) + "]";
        return "// <auto-generated/>\n#nullable enable\nnamespace EplanMcp.Contracts;\npublic static class ApiTryCatalog\n{\n"
            + "    public static bool Allowed(string group) => group switch\n    {\n"
            + string.Join("\n", data.Entries.Where(e => e.Row.ExcludedReason is null && e.Row.Kind is "method" or "property").Select(e => "        " + Literal(e.Row.Group) + " => true,"))
            + "\n        _ => false,\n    };\n    public static string[]? Shape(string group) => group switch\n    {\n"
            + string.Join("\n", selected.Select(s => "        " + Literal(s.Entry.Row.Group) + " => [" + string.Join(", ", new[] { s.Entry.Type[2..], s.Member.Signature! }.Concat(s.Member.Parameters!.Select(p => p.Name)).Select(Literal)) + "],"))
            + "\n        _ => null,\n    };\n    public static string[]? Shape(string group, System.Collections.Generic.IEnumerable<string> names) => Shape(group) ?? ((group, string.Join(\"|\", System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(names, n => n != \"target\"), n => n, System.StringComparer.Ordinal))) switch\n    {\n"
            + string.Join("\n", methods.Select(s => "        (" + Literal(s.Entry.Row.Group) + ", " + Literal(Names(s.Member)) + ") => [" + string.Join(", ", new[] { s.Entry.Type[2..], s.Member.Signature! }.Concat(s.Member.Parameters!.Select(ParameterName)).Select(Literal)) + "],"))
            + "\n        _ => null,\n    });\n    public static string? MethodAccess(string group) => group switch\n    {\n"
            + string.Join("\n", methods.DistinctBy(s => s.Entry.Row.Group).Select(s => "        " + Literal(s.Entry.Row.Group) + " => " + Literal(ReadMethod(s.Entry) ? "read" : "write") + ","))
            + "\n        _ => null,\n    };\n    public static string[]? ParameterShape(string signature, string name) => (signature, name) switch\n    {\n"
            + string.Join("\n", methods.SelectMany(s => s.Member.Parameters!.Select(p => "        (" + Literal(s.Member.Signature!) + ", " + Literal(ParameterName(p)) + ") => " + Shape(p.Type) + ",")))
            + "\n        _ => null,\n    };\n    public static string[]? ValueShape(string group) => group switch\n    {\n"
            + string.Join("\n", selected.Where(s => Writable(s.Member) && s.Member.ReturnType != "System.Int64").Select(s => "        " + Literal(s.Entry.Row.Group) + " => [" + string.Join(", ", (enums.TryGetValue(s.Member.ReturnType!, out string[]? values) ? _enumShape.Concat(values) : [s.Member.ReturnType!]).Select(Literal)) + "],"))
            + "\n        _ => null,\n    };\n    public static bool Service(string group) => group switch { " + string.Join(" ", methods.Where(s => services.ContainsKey(s.Entry.Type[2..])).DistinctBy(s => s.Entry.Row.Group).Select(s => Literal(s.Entry.Row.Group) + " => true,")) + " _ => false };\n"
            + "    public static string[] TargetKinds(string group) => Service(group) ? [\"project\"] : Kinds(Shape(group)?[0] ?? (group switch { " + string.Join(" ", methods.DistinctBy(s => s.Entry.Row.Group).Select(s => Literal(s.Entry.Row.Group) + " => " + Literal(s.Entry.Type[2..]) + ",")) + " _ => null }));\n    public static string[] Kinds(string? type) => type switch\n    {\n"
            + string.Join("\n", kinds.Select(p => "        " + Literal("Eplan.EplApi.DataModel." + p.Key) + " => [" + string.Join(", ", p.Value.Select(Literal)) + "],"))
            + "\n        _ => [],\n    };\n"
            + "    public static bool ValidValue(string[]? shape, string kind, string text) => shape?[0] switch\n    {\n"
            + "        \"System.Boolean\" => kind == \"Boolean\",\n        \"System.String\" => kind == \"String\",\n"
            + "        \"System.Int32\" => kind == \"Integer\" && int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),\n"
            + "        \"System.Int64\" => kind == \"Integer\" && long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),\n"
            + "        \"System.Double\" => (kind == \"Integer\" || kind == \"Float\") && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) && !double.IsNaN(n) && !double.IsInfinity(n),\n"
            + "        \"enum\" => kind == \"String\" && System.Linq.Enumerable.Contains(System.Linq.Enumerable.Skip(shape, 1), text, System.StringComparer.Ordinal),\n"
            + "        \"Eplan.EplApi.Base.PointD\" => kind == \"PointD\",\n        _ => shape is not null && Kinds(shape[0]).Length > 0 && kind == \"locator\",\n    };\n}\n";
    }
    private static string Target(ApiEntry entry) => "((global::" + entry.Type[2..] + ")target)." + entry.Name;
    internal static string Dispatch(string root)
    {
        Dictionary<string, string[]> enums = Enums(root);
        ApiIndexData data = Data(root);
        Dictionary<string, string[]> kinds = ObjectKindGenerator.Kinds(root);
        Dictionary<string, ApiTryServices.Service> services = ApiTryServices.Load(root);
        (ApiEntry Entry, ApiDetail Member)[] selected = [.. Selected(data, enums, kinds)];
        string Argument(ApiParameter p) => enums.TryGetValue(p.Type, out string[]? values)
            ? "(parameters[" + Literal(ParameterName(p)) + "]!.Value<string>() switch { " + string.Join(", ", values.Select(v => Literal(v) + " => global::" + p.Type + "." + v)) + ", _ => throw new System.InvalidOperationException(\"Invalid enum value.\") })"
            : ObjectType(p.Type, kinds) ? "((global::" + p.Type + ")resolve(parameters[" + Literal(ParameterName(p)) + "]!, " + Literal(p.Type) + "))"
            : p.Type == "Eplan.EplApi.Base.PointD" ? "new global::Eplan.EplApi.Base.PointD(parameters[" + Literal(ParameterName(p)) + "]![\"x\"]!.Value<double>(), parameters[" + Literal(ParameterName(p)) + "]![\"y\"]!.Value<double>())"
            : "parameters[" + Literal(ParameterName(p)) + "]!.Value<" + p.Type + ">()";
        return "// <auto-generated/>\n#nullable enable\nusing Eplan.EplApi.DataModel;\nusing Newtonsoft.Json.Linq;\nnamespace EplanMcp.Bridge.Eplan.Adapters;\ninternal static class ApiTryDispatch\n{\n"
        + "    internal static string? Read(string group, StorableObject target, JObject parameters) => System.Convert.ToString(group switch\n    {\n"
        + string.Join("\n", selected.Select(s => "        " + Literal(s.Entry.Row.Group) + " => (object)" + (s.Member.Accessors == "set" ? "((global::Eplan.EplApi.DataModel.Function)target).FunctionCategory" : Target(s.Entry))
            + (s.Entry.Row.Kind == "method" ? "(" + string.Join(", ", s.Member.Parameters!.Select(p => "parameters[" + Literal(p.Name) + "]!.Value<bool>()")) + ")" : "") + ","))
        + "\n        _ => throw new System.InvalidOperationException(\"No compiled dispatch for this group and shape.\"),\n    }, System.Globalization.CultureInfo.InvariantCulture);\n"
        + "    internal static void Write(string group, StorableObject target, JObject parameters)\n    {\n        switch (group)\n        {\n"
        + string.Join("\n", selected.Where(s => Writable(s.Member) && s.Member.ReturnType != "System.Int64").Select(s => "            case " + Literal(s.Entry.Row.Group) + ": " + Target(s.Entry) + " = "
            + (enums.TryGetValue(s.Member.ReturnType!, out string[]? values)
                ? "parameters[\"value\"]!.Value<string>() switch { " + string.Join(", ", values.Select(v => Literal(v) + " => global::" + s.Member.ReturnType + "." + v)) + ", _ => throw new System.InvalidOperationException(\"Invalid enum value.\") }"
                : "parameters[\"value\"]!.Value<" + s.Member.ReturnType + ">()") + "; break;"))
        + "\n            default: throw new System.InvalidOperationException(\"No compiled setter for this group.\");\n        }\n    }\n    internal static object? Invoke(string signature, StorableObject target, JObject parameters, System.Func<JToken, string, StorableObject> resolve)\n    {\n        switch (signature)\n        {\n"
        + string.Join("\n", Methods(data, enums, kinds, services).Select(s =>
        {
            bool service = services.TryGetValue(s.Entry.Type[2..], out ApiTryServices.Service? receiver);
            string construct = service ? (receiver!.Disposable ? "using " : "") + "var service = new global::" + s.Entry.Type[2..] + "(); " : "";
            return "            case " + Literal(s.Member.Signature!) + ": { " + construct + (s.Member.ReturnType == "System.Void" ? "" : "return ")
                + (service ? "service." + s.Entry.Name : Target(s.Entry)) + "(" + string.Join(", ", s.Member.Parameters!.Select(Argument)) + ");" + (s.Member.ReturnType == "System.Void" ? " return null;" : "") + " }";
        }))
        + "\n            default: throw new System.InvalidOperationException(\"No compiled method for this signature.\");\n        }\n    }\n}\n";
    }
}
