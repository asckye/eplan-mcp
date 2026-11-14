using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage;

internal static partial class ApiTryGenerator
{
    internal static ApiTryContribution HandlesContribution(SliceInput input) => new()
    {
        Members = SettingsOverlap(input, input.Handles.Value.Members),
        TableRows = { [ApiTrySlot.HandleCatalog] = (_, _) => HandleCatalog(input.Root) },
        DispatchFragments = { [ApiTrySlot.HandleDispatch] = (_, _) => HandleDispatch(input.Root) },
        Templates = { [ApiTrySlot.HandleReport] = (_, original) => HandleReport(input.Root, original) },
    };
    internal sealed record HandleModel(Dictionary<string, JsonElement> Types, HashSet<string> Reachable, (ApiEntry Entry, ApiDetail Member)[] Members);
    private static bool MatchHandle(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // ADR-0054: parts master data handles start at the configured database; the HEServices master data services sync projects and stay pending.
    internal static string? HandleRestriction(string type) => MatchHandle(type, "^Eplan\\.EplApi\\.HEServices\\.(Masterdata|PartsService)(\\.|$)") ? "masterdata_copy_pending"
        : MatchHandle(type, "\\.(ApplicationFramework|Gui|System|.*Macro|.*File|.*Stream|.*Transaction|.*Lock|.*SafetyPoint)(\\.|$)") ? "handle_type_excluded" : null;
    internal static HandleModel BuildDirectHandles(ApiIndexData data, JsonElement[] metadata, Dictionary<string, string[]> kinds, Dictionary<string, string[]> enums, Dictionary<string, ApiTryServices.Service> services)
    {
        string Text(JsonElement t, string key) => t.TryGetProperty(key, out JsonElement v) ? v.GetString() ?? "" : "";
        var types = metadata.ToDictionary(t => Text(t, "id")[2..], StringComparer.Ordinal);
        bool Public(string type) => types.TryGetValue(type, out JsonElement t) && !t.TryGetProperty("access", out _) && !t.TryGetProperty("excluded", out _)
            && (!t.TryGetProperty("outer", out JsonElement outer) || Public(outer.GetString()![2..]));
        bool Candidate(string? type) => type is not null && Public(type) && Text(types[type], "kind") is "class" or "struct" or "interface" && HandleRestriction(type) is null;
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string[]> Expanded() => kinds.Concat(reachable.Where(t => !ObjectType(t, kinds)).Select(t => new KeyValuePair<string, string[]>(t.Replace("Eplan.EplApi.DataModel.", "", StringComparison.Ordinal), []))).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        bool Receiver(ApiEntry e) => ObjectType(e.Type[2..], kinds) || reachable.Contains(e.Type[2..]) || services.TryGetValue(e.Type[2..], out ApiTryServices.Service? s) && s.Reason is null;
        string? Restriction(ApiEntry e, ApiDetail d)
        {
            if ((HandleRestriction(e.Type[2..]) ?? HandleRestriction(d.ReturnType ?? "")) is { } reason)
            { return reason; }
            if (SettingType(e.Type) && (e.Row.Kind == "method" ? !EntryRead(e) : d.Accessors?.Contains("get", StringComparison.Ordinal) != true))
            { return "settings_read_only"; }
            return SettingType(e.Type) ? null : ApiTryServices.Exclusion(e, d);
        }
        bool Eligible(ApiEntry e, ApiDetail d) => Receiver(e) && (e.Row.Kind == "method" || ObjectType(e.Type[2..], kinds) || reachable.Contains(e.Type[2..])) && Restriction(e, d) is null && Exclusion(e, d, enums, Expanded(), reachable.Contains(e.Type[2..]) ? null : services) is null
            && (e.Row.Kind == "method" || e.Row.Kind == "property" && d.Parameters is { Length: 0 } && d.Accessors?.Split(';').Contains("get", StringComparer.Ordinal) == true);
        IEnumerable<string> Produced(ApiDetail d) => ProducedTypes(d).Where(Candidate);
        bool changed;
        do
        {
            changed = false;
            // Lifecycle calls may reuse independently reachable handles, but must not introduce new handle roots.
            foreach (ApiEntry e in data.Entries.Where(e => Receiver(e) && !(e.Row.Kind == "method" && Lifecycle(e))))
            {
                foreach (ApiDetail d in e.Details.Where(d => d.Kind == "overload" && Produced(d).Any()))
                {
                    var expanded = Expanded();
                    foreach (string produced in Produced(d))
                    { expanded[produced.Replace("Eplan.EplApi.DataModel.", "", StringComparison.Ordinal)] = []; }
                    if (e.Row.Kind == "property" && !ObjectType(e.Type[2..], kinds) && !reachable.Contains(e.Type[2..]) || Restriction(e, d) is not null || Exclusion(e, d, enums, expanded, reachable.Contains(e.Type[2..]) ? null : services) is not null || e.Row.Kind == "property" && (d.Parameters is not { Length: 0 } || d.Accessors?.Split(';').Contains("get", StringComparer.Ordinal) != true))
                    { continue; }
                    foreach (string produced in Produced(d))
                    {
                        string type = produced;
                        while (Candidate(type))
                        { changed |= reachable.Add(type); type = Text(types[type], "base"); }
                    }
                }
            }
        } while (changed);
        var existing = Selected(data, enums, kinds).Concat(Methods(data, enums, kinds, services)).Select(s => s.Member.Id).ToHashSet(StringComparer.Ordinal);
        var members = data.Entries.SelectMany(e => e.Details.Where(d => d.Kind == "overload" && Eligible(e, d)).GroupBy(Names, StringComparer.Ordinal).Where(g => g.Count() == 1)
            .Select(g => (Entry: e, Member: g.Single()))).Where(s => !existing.Contains(s.Member.Id)
                && !data.Entries.Where(e => e == s.Entry).SelectMany(e => e.Details).Any(d => existing.Contains(d.Id) && Names(d) == Names(s.Member))).ToArray();
        return new(types, reachable, members);
    }
    private static HandleModel Handles(string root) =>
        RunCache.Get(root, () => HandlesCore(root));

    private static HandleModel HandlesCore(string root)
    {
        string typesFile = Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl");
        HandleModel model = BuildHandles(Data(root), [.. (File.Exists(typesFile) ? File.ReadLines(typesFile) : []).Select(l => JsonSerializer.Deserialize<JsonElement>(l))], ObjectKindGenerator.Kinds(root), Enums(root), ApiTryServices.Load(root));
        var entries = EntrySelected(EntryData(root));
        return model with { Members = [.. model.Members.Where(s => !entries.Any(e => e.Entry.Row.Group == s.Entry.Row.Group && Names(e.Member) == Names(s.Member)))] };
    }
    // A service property is read on a fresh instance per call; a value set on it is lost, or reaches shared state behind it.
    private static bool ServiceRoot(ApiEntry e, HandleModel model, Dictionary<string, ApiTryServices.Service> services) =>
        services.TryGetValue(e.Type[2..], out ApiTryServices.Service? service) && service.Reason is null && !model.Reachable.Contains(e.Type[2..]);
    private static bool HandleSetter(ApiEntry e, ApiDetail d) => !HandleCallable(e, d) && Writable(d) && !SettingType(e.Type) && d.ReturnType != "System.Int64";
    private static string HandleCatalog(string root)
    {
        HandleModel model = Handles(root);
        Dictionary<string, ApiTryServices.Service> serviceTypes = ApiTryServices.Load(root);
        Dictionary<string, string[]> enums = Enums(root);
        string Shape(string type) => CollectionShape(type, enums);
        string Switch(string name, IEnumerable<string> rows, string fallback = "null") => "    public static " + name + " switch { " + string.Join(" ", rows) + " _ => " + fallback + " };\n";
        string Fields(ApiEntry e, ApiDetail d) => "[" + string.Join(", ", CallShape(e, d).Select(Literal)) + "]";
        return Switch("bool HandleType(string type) => type", model.Reachable.Select(t => Literal(t) + " => true,"), "false")
            + Switch("string[]? HandleShape(string group) => group", model.Members.Where(s => HandleProperty(s.Entry) && !HandleCallable(s.Entry, s.Member)).Select(s => Literal(s.Entry.Row.Group) + " => " + Fields(s.Entry, s.Member) + ","))
            + Switch("string[]? HandleMethodShape(string group, System.Collections.Generic.IEnumerable<string> names) => (group, string.Join(\"|\", System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(names, n => n != \"target\"), n => n, System.StringComparer.Ordinal)))", model.Members.Where(s => HandleCallable(s.Entry, s.Member)).Select(s => "(" + Literal(s.Entry.Row.Group) + ", " + Literal(Names(s.Member)) + ") => " + Fields(s.Entry, s.Member) + ","))
            + Switch("string? HandleMethodAccess(string group) => Shape(group) is not null ? null : group", model.Members.Where(s => HandleCallable(s.Entry, s.Member)).DistinctBy(s => s.Entry.Row.Group).Select(s => Literal(s.Entry.Row.Group) + " => " + Literal(HandleRead(s.Entry) ? "read" : "write") + ","))
            + Switch("string[]? HandleParameterShape(string signature, string name) => (signature, name)", model.Members.Where(s => HandleCallable(s.Entry, s.Member)).SelectMany(s => Inputs(s.Member).Select(p => "(" + Literal(s.Member.Signature!) + ", " + Literal(ParameterName(p)) + ") => " + Shape(p.Type) + ",")))
            + Switch("string[]? HandleValueShape(string group) => group", model.Members.Where(s => s.Entry.Row.Kind == "property" && ChainSetter(s.Entry, s.Member) && !ServiceRoot(s.Entry, model, serviceTypes) && Scalar(s.Member.ReturnType, enums)).Select(s => Literal(s.Entry.Row.Group) + " => " + Shape(s.Member.ReturnType!) + ","))
            + Switch("bool HandleService(string group) => group", model.Members.Where(s => serviceTypes.TryGetValue(s.Entry.Type[2..], out ApiTryServices.Service? service) && service.Reason is null && !model.Reachable.Contains(s.Entry.Type[2..])).DistinctBy(s => s.Entry.Row.Group).Select(s => Literal(s.Entry.Row.Group) + " => true,"), "false");
    }
    private static string HandleDispatch(string root)
    {
        HandleModel model = Handles(root);
        Dictionary<string, string[]> enums = Enums(root);
        Dictionary<string, string[]> kinds = ObjectKindGenerator.Kinds(root);
        Dictionary<string, ApiTryServices.Service> services = ApiTryServices.Load(root);
        ApiIndexData data = Data(root);
        bool Struct(string t) => model.Types.TryGetValue(t, out JsonElement metadata) && metadata.GetProperty("kind").GetString() == "struct";
        string Cast(string t, string value) => Struct(t) ? "((System.Runtime.CompilerServices.StrongBox<global::" + t + ">)" + value + ").Value" : "((global::" + t + ")" + value + ")";
        var expanded = kinds.Concat(model.Reachable.Where(t => !ObjectType(t, kinds)).Select(t => new KeyValuePair<string, string[]>(t.Replace("Eplan.EplApi.DataModel.", "", StringComparison.Ordinal), []))).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        HashSet<string> structs = [.. model.Reachable.Where(Struct)];
        string Argument(string name, string type) => ArgumentValue(type, "parameters[" + Literal(name) + "]", enums, expanded, structs);
        return "internal static class ApiTryHandleDispatch\n{\n" + ChainReturnTypes(model) + "    internal static bool Compatible(object value, string type) => type switch { " + string.Join(" ", model.Reachable.Select(t => Literal(t) + " => value is " + (Struct(t) ? "System.Runtime.CompilerServices.StrongBox<global::" + t + ">" : "global::" + t) + ",")) + " _ => false };\n"
            + "    internal static string? Type(object value) => value.GetType() switch { " + string.Join(" ", model.Reachable.Select(t => "var t when t == typeof(global::" + t + ") => " + Literal(t) + ",")) + " _ => null };\n"
            + "    internal static object Box(object value) => value switch { " + string.Join(" ", model.Reachable.Where(Struct).Select(t => "global::" + t + " v => new System.Runtime.CompilerServices.StrongBox<global::" + t + ">(v),")) + " _ => value };\n"
            + "    internal static object? Owner(object value) => value switch { " + string.Join(" ", model.Reachable.Select(t => (Type: t, Parent: data.Entries.FirstOrDefault(e => e.Type == "T:" + t && e.Name is "Project" or "ParentFunction" or "Parent" && e.Details.Any(d => d.Accessors?.Contains("get", StringComparison.Ordinal) == true && ObjectType(d.ReturnType, kinds) && d.Access is null && d.ExcludedReason is null && !d.Static && d.Parameters is { Length: 0 })))).Where(p => p.Parent is not null).Select(p => "global::" + p.Type + " v" + ChainOwnerGuard(p.Type, model) + " => v." + p.Parent!.Name + ",")) + " _ => value };\n"
            + "    internal static object? Invoke(string signature, object target, JObject parameters, System.Func<JToken, string, object> resolve)\n    {\n        switch (signature)\n        {\n"
            + string.Join("\n", model.Members.Select(s =>
            {
                if (s.Entry.Row.Kind == "method")
                {
                    bool serviceReceiver = services.TryGetValue(s.Entry.Type[2..], out ApiTryServices.Service? helper) && helper.Reason is null && !model.Reachable.Contains(s.Entry.Type[2..]);
                    return MethodCase(s.Entry, s.Member, enums, expanded, services, serviceReceiver ? null : Cast(s.Entry.Type[2..], "target"), structs);
                }
                string receiver = Cast(s.Entry.Type[2..], "target") + "." + s.Entry.Name;
                string construct = "";
                if (services.TryGetValue(s.Entry.Type[2..], out ApiTryServices.Service? service) && service.Reason is null && !model.Reachable.Contains(s.Entry.Type[2..]))
                {
                    construct = (service.Disposable ? "using " : "") + "var service = new global::" + (service.Concrete ?? s.Entry.Type[2..])
                        + (service.ProjectConstructor ? "((global::Eplan.EplApi.DataModel.Project)target!); " : "(); ");
                    receiver = "service." + s.Entry.Name;
                }
                string call = ChainCall(s.Entry, s.Member, receiver, Inputs(s.Member).Select(p => Argument(ParameterName(p), p.Type)));
                string setter = s.Entry.Row.Kind == "property" && ChainSetter(s.Entry, s.Member) && !ServiceRoot(s.Entry, model, services) && Scalar(s.Member.ReturnType, enums) ? "if (parameters.ContainsKey(\"value\")) { " + receiver + " = " + Argument("value", s.Member.ReturnType!) + "; } " : "";
                return "            case " + Literal(s.Member.Signature!) + ": { " + construct + setter + (s.Member.ReturnType == "System.Void" ? call + "; return null;" : "return " + call + ";") + " }";
            })) + "\n            default: throw new System.InvalidOperationException(\"No compiled handle dispatch.\");\n        }\n    }\n}\n";
    }
    private static string HandleReport(string root, string original)
    {
        HandleModel model = Handles(root);
        Dictionary<string, string[]> enums = Enums(root);
        ApiIndexData data = Data(root);
        var added = model.Members.Select(s => s.Member.Id).ToHashSet(StringComparer.Ordinal);
        string[] lines = original.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            string[] row = lines[i].Split('\t');
            if (row.Length == 4 && row[3] is "unsupported_return" or "unsupported_parameter" && data.Entries.FirstOrDefault(e => e.Row.Group == row[0]) is { } entry && entry.Details.FirstOrDefault(d => d.Id == row[1]) is { } member && (HandleRestriction(entry.Type[2..]) ?? HandleRestriction(member.ReturnType ?? "")) == "masterdata_copy_pending")
            { lines[i] = row[0] + "\t" + row[1] + "\texcluded\tmasterdata_copy_pending"; }
            else if (row.Length == 4 && added.Contains(row[1]))
            { lines[i] = row[0] + "\t" + row[1] + "\t" + HandleClassification(model.Members.First(s => s.Member.Id == row[1]).Entry); }
        }
        var reported = lines.Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')[1]).ToHashSet(StringComparer.Ordinal);
        var unreported = model.Members.Concat(Selected(data, enums, ObjectKindGenerator.Kinds(root))).DistinctBy(s => s.Member.Id).Where(s => !reported.Contains(s.Member.Id)).ToArray();
        reported.UnionWith(unreported.Select(s => s.Member.Id));
        return string.Join("\n", lines) + string.Join("", unreported.Select(s => s.Entry.Row.Group + "\t" + s.Member.Id + "\t" + (HandleCallable(s.Entry, s.Member) ? HandleClassification(s.Entry) : "read\tgetter") + "\n"
            + (s.Entry.Row.Kind == "property" && ChainReportSetter(s.Entry, s.Member, model) && !ServiceRoot(s.Entry, model, ApiTryServices.Load(root)) && Scalar(s.Member.ReturnType, enums) ? s.Entry.Row.Group + "\t" + s.Member.Id + ":set\twrite\tsetter\n" : ""))) + string.Join("", data.Entries.SelectMany(e => e.Details.Where(d => d.Kind == "overload" && !reported.Contains(d.Id) && (HandleRestriction(e.Type[2..]) ?? HandleRestriction(d.ReturnType ?? "")) == "masterdata_copy_pending").Select(d => e.Row.Group + "\t" + d.Id + "\texcluded\t" + ((e.Row.ExcludedReason ?? d.ExcludedReason) is { } reason ? "catalog:" + reason : "masterdata_copy_pending") + "\n")));
    }
}
