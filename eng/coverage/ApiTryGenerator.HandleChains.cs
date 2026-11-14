using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage;

internal static partial class ApiTryGenerator
{
    internal static ApiTryContribution ChainsContribution(SliceInput input) => new()
    {
        // Handles emits these members; chains adds reachability, return/element metadata and reports.
        Members = Claims(input.Handles.Value.Members, ApiTryClaim.Decorate),
        TableRows = { [ApiTrySlot.ChainCatalog] = (_, _) => ChainCatalog(input.Root) },
        Templates = { [ApiTrySlot.ChainGaps] = (_, _) => LegacyChainGapReport(input.Root), [ApiTrySlot.ChainGapsBefore] = (_, _) => LegacyChainGapReport(input.Root, true), [ApiTrySlot.ChainReachability] = (_, _) => LegacyChainReachabilityReport(input.Root) },
    };
    private sealed record ChainAnalysis(HandleModel Model, Dictionary<string, (int Depth, string Source)> Paths);
    private static string? ElementType(string? type, Dictionary<string, JsonElement> types)
    {
        if (type is null)
        { return null; }
        if (type.EndsWith("[]", StringComparison.Ordinal))
        { return type[..^2]; }
        Match match = Regex.Match(type, @"^System.Collections.Generic.(?:IEnumerable|ICollection|IList|IReadOnlyCollection|IReadOnlyList|List)\{([^{} ,]+)\}$", RegexOptions.CultureInvariant);
        if (match.Success)
        { return match.Groups[1].Value; }
        return types.TryGetValue(type, out JsonElement t) && t.TryGetProperty("interfaces", out JsonElement interfaces)
            ? interfaces.EnumerateArray().Select(i => ElementType(i.GetString(), types)).FirstOrDefault(e => e is not null) : null;
    }
    internal static HandleModel BuildHandles(ApiIndexData data, JsonElement[] metadata, Dictionary<string, string[]> kinds, Dictionary<string, string[]> enums, Dictionary<string, ApiTryServices.Service> services) => AnalyzeChains(data, metadata, kinds, enums, services).Model;
    private static ChainAnalysis AnalyzeChains(ApiIndexData data, JsonElement[] metadata, Dictionary<string, string[]> kinds, Dictionary<string, string[]> enums, Dictionary<string, ApiTryServices.Service> services)
    {
        string Text(JsonElement t, string key) => t.TryGetProperty(key, out JsonElement v) ? v.GetString() ?? "" : "";
        var types = metadata.ToDictionary(t => Text(t, "id")[2..], StringComparer.Ordinal);
        bool Public(string type) => types.TryGetValue(type, out JsonElement t) && !t.TryGetProperty("access", out _) && !t.TryGetProperty("excluded", out _)
            && (!t.TryGetProperty("outer", out JsonElement outer) || Public(outer.GetString()![2..]));
        bool Candidate(string? type) => type is not null && Public(type) && Text(types[type], "kind") is "class" or "struct" or "interface" && HandleRestriction(type) is null;
        IEnumerable<string> Parents(string type)
        {
            if (!types.TryGetValue(type, out JsonElement t))
            { yield break; }
            if (t.TryGetProperty("base", out JsonElement parent))
            { yield return parent.GetString()!; }
            if (t.TryGetProperty("interfaces", out JsonElement interfaces))
            { foreach (JsonElement i in interfaces.EnumerateArray()) { yield return i.GetString()!; } }
        }
        IEnumerable<string> Ancestors(string type) => new[] { type }.Concat(Parents(type).Where(Candidate).SelectMany(Ancestors));
        // A member declared to return a class may return any public subclass at run time; handles carry the run-time type.
        var subclasses = types.Values.Where(t => Text(t, "kind") == "class" && Text(t, "base").Length > 0).GroupBy(t => Text(t, "base"), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(t => Text(t, "id")[2..]).ToArray(), StringComparer.Ordinal);
        IEnumerable<string> Subclasses(string type) => (subclasses.TryGetValue(type, out string[]? children) ? children : []).Where(Candidate).SelectMany(c => new[] { c }.Concat(Subclasses(c)));
        var paths = new Dictionary<string, (int Depth, string Source)>(StringComparer.Ordinal);
        foreach (string root in kinds.Keys.Select(k => "Eplan.EplApi.DataModel." + k))
        { foreach (string type in Ancestors(root).Where(t => Text(types[t], "kind") == "interface")) { paths.TryAdd(type, (0, "locator:" + root)); } }
        int Depth(string t) => ObjectType(t, kinds) || services.TryGetValue(t, out ApiTryServices.Service? s) && s.Reason is null ? 0 : paths.TryGetValue(t, out var p) ? p.Depth : 99;
        Dictionary<string, string[]> Expanded() => kinds.Concat(paths.Keys.Where(t => !ObjectType(t, kinds)).Select(t => new KeyValuePair<string, string[]>(t.Replace("Eplan.EplApi.DataModel.", "", StringComparison.Ordinal), []))).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        Dictionary<string, string[]> expanded = Expanded();
        bool Safe(ApiEntry e, ApiDetail d)
        {
            if (MatchHandle(e.Name, "^(Destroy|Init|Freeze|Lock)"))
            { return false; }
            if (Depth(e.Type[2..]) > 4 || HandleRestriction(e.Type[2..]) is not null || HandleRestriction(ElementType(d.ReturnType, types) ?? d.ReturnType ?? "") is not null)
            { return false; }
            if (e.Row.Kind != "method" && !ObjectType(e.Type[2..], kinds) && !paths.ContainsKey(e.Type[2..])
                // A service is constructed and disposed per call: only scalar properties, never a handle into a disposed service.
                && !(services.TryGetValue(e.Type[2..], out ApiTryServices.Service? root) && root.Reason is null && Scalar(d.ReturnType, enums)))
            { return false; }
            string? restriction = ApiTryServices.Exclusion(e, d);
            bool settings = ChainSettingType(e.Type);
            if (restriction is not null && !(restriction == "settings_or_scheme_parameter" && (settings || HandleProperty(e) && MatchHandle(d.ReturnType ?? "", "Setting")))
                && !(settings && HandleRead(e) && restriction == "external_path_parameter" && d.Parameters!.All(p => p.Type != "System.String" || p.Name is "strSettingPath" or "strSubNodePath")))
            { return false; }
            if (settings && e.Row.Kind == "method" && !HandleRead(e))
            { return false; }
            return e.Row.Kind is "property" or "method" && (e.Row.Kind == "method" || d.Accessors?.Split(';').Contains("get", StringComparer.Ordinal) == true && d.Parameters!.All(p => Scalar(p.Type, enums) || p.Type is "System.Int16" or "System.UInt32"))
                && Exclusion(e, d with { ReturnType = "System.Void", Parameters = [.. d.Parameters!.Select(p => Out(p) ? p with { Type = "System.Int32@" } : e.Row.Kind == "property" && p.Type is "System.Int16" or "System.UInt32" ? p with { Type = "System.Int64" } : p)] }, enums, expanded, paths.ContainsKey(e.Type[2..]) ? null : services) is null;
        }
        bool Return(string? type, bool discover) => type == "System.Void" || Scalar(type, enums) || ResultScalar(type) || type == "Eplan.EplApi.Base.PointD" || ObjectType(type, kinds)
            || (discover ? Candidate(type) : paths.ContainsKey(type ?? "")) || ElementType(type, types) is { } element && (Scalar(element, enums) || ObjectType(element, kinds) || (discover ? Candidate(element) : paths.ContainsKey(element)));
        bool Returns(ApiDetail d, bool discover) => Return(d.ReturnType, discover) && d.Parameters!.Where(Out).All(p => Return(ValueType(p), discover));
        bool changed;
        do
        {
            var additions = new List<(string Type, int Depth, string Source)>();
            // A69: lifecycle calls are dispatchable writes now; like direct handles they may reuse reachable types but never seed one.
            foreach (ApiEntry e in data.Entries.Where(e => !(e.Row.Kind == "method" && Lifecycle(e))))
            {
                foreach (var group in e.Details.Where(d => d.Kind == "overload" && Safe(e, d) && Returns(d, true)).GroupBy(Names, StringComparer.Ordinal).Where(g => g.Count() == 1))
                {
                    ApiDetail d = group.Single();
                    foreach (string? returned in new[] { d.ReturnType }.Concat(d.Parameters!.Where(Out).Select(ValueType)).SelectMany(t => new[] { t, ElementType(t, types) }))
                    {
                        if (Candidate(returned))
                        { additions.AddRange(Ancestors(returned!).Concat(Subclasses(returned!).SelectMany(Ancestors)).Select(type => (type, Depth(e.Type[2..]) + 1, d.Id + (returned != d.ReturnType ? " [element]" : "") + (type != returned && !Ancestors(returned!).Contains(type) ? " [subclass]" : "")))); }
                    }
                }
            }
            changed = false;
            foreach (var a in additions.Where(a => a.Depth <= 4))
            {
                if (!paths.TryGetValue(a.Type, out var prior) || a.Depth < prior.Depth)
                { paths[a.Type] = (a.Depth, a.Source); changed = true; }
            }
            expanded = Expanded();
        } while (changed);
        var existing = Selected(data, enums, kinds).Concat(Methods(data, enums, kinds, services)).Select(s => s.Member.Id).ToHashSet(StringComparer.Ordinal);
        var members = data.Entries.SelectMany(e => e.Details.Where(d => d.Kind == "overload" && Safe(e, d) && Returns(d, false)).GroupBy(Names, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => (Entry: e, Member: g.Single())))
            .Where(s => !existing.Contains(s.Member.Id) && !s.Entry.Details.Any(d => existing.Contains(d.Id) && Names(d) == Names(s.Member))).ToArray();
        return new(new(types, paths.Keys.ToHashSet(StringComparer.Ordinal), members), paths);
    }
    private static bool HandleCallable(ApiEntry e, ApiDetail d) => e.Row.Kind == "method" || d.Parameters is { Length: > 0 };
    private static bool HandleProperty(ApiEntry e) => e.Row.Kind == "property";
    private static bool ChainSettingType(string type) => SettingType(type) || MatchHandle(type, "Setting");
    private static bool HandleRead(ApiEntry e) => HandleProperty(e) || ReadMethod(e) || ChainSettingType(e.Type) && _readPrefixes.Any(p => e.Name.StartsWith(p, StringComparison.Ordinal))
        && !e.Details.Where(d => d.Kind == "overload").SelectMany(d => d.Parameters!).Any(p => p.Name is not ("strSettingPath" or "strSubNodePath") && ApiTryServices.SideEffectParameter(p));
    private static string HandleClassification(ApiEntry e) => HandleRead(e) && !ReadMethod(e) ? "read\tsettings_key_read" : Classification(e, false);
    private static bool ChainWritable(ApiEntry e, ApiDetail d) => e.Row.Kind == "property" && !ChainSettingType(e.Type) && ApiTryServices.Exclusion(e, d with { Parameters = [new(e.Name, d.ReturnType!)] }) is null;
    private static bool ChainSetter(ApiEntry e, ApiDetail d) => HandleSetter(e, d) && ChainWritable(e, d);
    private static bool ChainReportSetter(ApiEntry e, ApiDetail d, HandleModel model) => model.Members.Any(m => m.Member.Id == d.Id) ? ChainSetter(e, d) : HandleSetter(e, d);
    internal static string ChainReturnTypes(HandleModel model) => "    internal static string? ReturnType(string signature) => signature switch { " + string.Join(" ", model.Members.Where(s => model.Reachable.Contains(s.Member.ReturnType ?? "")).Select(s => Literal(s.Member.Signature!) + " => " + Literal(s.Member.ReturnType!) + ",")) + " _ => null };\n"
        + "    internal static object? Elements(string signature, object? value) => signature switch { " + string.Join(" ", model.Members.Where(s => !s.Member.Parameters!.Any(p => Out(p) || p.Direction == "ref" || p.Type.EndsWith('@')) && ElementType(s.Member.ReturnType, model.Types) is { } element && model.Reachable.Contains(element)).Select(s => Literal(s.Member.Signature!) + " => new ApiTryNativeElements(value, " + (model.Reachable.Contains(s.Member.ReturnType!) ? "true" : "false") + ", " + Literal(ElementType(s.Member.ReturnType, model.Types)!) + "),")) + " _ => value };\n";
    private static string ChainOwnerGuard(string type, HandleModel model) => model.Types[type].GetProperty("kind").GetString() == "interface" ? " when Compatible(v, " + Literal(type) + ")" : " when v.GetType() == typeof(global::" + type + ")";
    private static string ChainCatalog(string root)
    {
        var input = ChainInputs(root);
        HandleModel model = BuildHandles(input.Data, input.Types, input.Kinds, input.Enums, input.Services);
        bool Implements(string t, string i) => t == i || model.Types.TryGetValue(t, out JsonElement metadata) && (metadata.TryGetProperty("base", out JsonElement b) && Implements(b.GetString()!, i) || metadata.TryGetProperty("interfaces", out JsonElement list) && list.EnumerateArray().Any(v => Implements(v.GetString()!, i)));
        return "    private static bool HandleKeyValue(string[]? shape, string kind, string text) => kind == \"Integer\" && (shape?[0] switch { \"System.Int16\" => short.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _), \"System.UInt32\" => uint.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _), _ => false });\n"
            + "    private static string[] HandleTargetKinds(string? type) => type switch { " + string.Join(" ", model.Reachable.Where(t => model.Types[t].GetProperty("kind").GetString() == "interface").Select(t => Literal(t) + " => [" + string.Join(", ", input.Kinds.Where(k => Implements("Eplan.EplApi.DataModel." + k.Key, t)).SelectMany(k => k.Value).Distinct(StringComparer.Ordinal).Select(Literal)) + "],")) + " _ => [] };\n";
    }
    private static string ChainCall(ApiEntry e, ApiDetail d, string receiver, IEnumerable<string> arguments)
    {
        string args = string.Join(", ", arguments);
        // Named parameterized CLR properties are emitted as get_Name; C# indexers use brackets.
        string call = e.Row.Kind == "method" ? receiver + "(" + args + ")" : d.Parameters is { Length: > 0 }
            // Every *PropertyList declares its Property member with IndexerName, so all of them take brackets; the
            // list used to name UniversalPropertyList and the master-data ones because only those were selectable.
            ? e.Name == "Item" || e.Type == "T:Eplan.EplApi.DataModel.Graphics.GraphicalLayerTable" && e.Name == "Layer"
                || e.Name == "Property" && e.Type.EndsWith("PropertyList", StringComparison.Ordinal)
                || (e.Type, e.Name) is ("T:Eplan.EplApi.MasterData.MDSymbol", "Variant") or ("T:Eplan.EplApi.MasterData.MDSymbolLibrary", "Symbol")
                ? receiver[..^(e.Name.Length + 1)] + "[" + args + "]" : receiver[..^e.Name.Length] + "get_" + e.Name + "(" + args + ")" : receiver;
        return call;
    }
    private static IEnumerable<JsonElement> ChainLines(string path) => File.Exists(path) ? File.ReadLines(path).Select(l => JsonSerializer.Deserialize<JsonElement>(l)) : [];
    private static JsonElement[] ChainTypes(string root) => [.. ChainLines(Path.Combine(root, "catalog/eplan-2026.0.3/types.jsonl"))];
    /// <summary>The catalogue's field members, which ADR-0055 reads as handle sources.</summary>
    internal static JsonElement[] ChainFields(string root) =>
        [.. ChainMemberFiles(root).SelectMany(ChainLines).Where(m => m.TryGetProperty("kind", out JsonElement k) && k.GetString() == "field")];
    private static IEnumerable<string> ChainMemberFiles(string root) => Directory.Exists(Path.Combine(root, "catalog/eplan-2026.0.3/members")) ? Directory.EnumerateFiles(Path.Combine(root, "catalog/eplan-2026.0.3/members"), "*.jsonl").Order(StringComparer.Ordinal) : [];
    private static (ApiIndexData Data, JsonElement[] Types, Dictionary<string, string[]> Kinds, Dictionary<string, string[]> Enums, Dictionary<string, ApiTryServices.Service> Services) ChainInputs(string root) =>
        (Data(root), ChainTypes(root), ObjectKindGenerator.Kinds(root), Enums(root), ApiTryServices.Load(root));
    private static (ApiEntry Entry, string Reason, string Detail)[] ChainGaps(string root, bool before)
    {
        var input = ChainInputs(root);
        HandleModel model = before ? BuildDirectHandles(input.Data, input.Types, input.Kinds, input.Enums, input.Services) : BuildHandles(input.Data, input.Types, input.Kinds, input.Enums, input.Services);
        // The entry model reaches types by its own property, field and keyed paths (ADR-0055, ADR-0056), which the
        // chain model above does not know about. Without it the gap list keeps reporting groups that are callable,
        // as PlaceHolder's variable and value-set classes were on 2026-11-14.
        EntryModel entries = EntryData(root);
        var entryReachable = entries.Paths.Select(p => p.Type).ToHashSet(StringComparer.Ordinal);
        var covered = model.Members.Concat(Selected(input.Data, input.Enums, input.Kinds)).Concat(Methods(input.Data, input.Enums, input.Kinds, input.Services))
            .Concat(EntrySelected(entries)).Select(s => s.Entry.Row.Group).ToHashSet(StringComparer.Ordinal);
        var expanded = input.Kinds.Concat(model.Reachable.Where(t => !ObjectType(t, input.Kinds)).Select(t => new KeyValuePair<string, string[]>(t.Replace("Eplan.EplApi.DataModel.", "", StringComparison.Ordinal), []))).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return [.. input.Data.Entries.Where(e => e.Row.Group.StartsWith("O:", StringComparison.Ordinal) && e.Row.Status is "registered" or "env_blocked" && !covered.Contains(e.Row.Group)).Select(e =>
        {
            string type = e.Type[2..];
            // A static member needs no receiver, so the declaring type's reachability says nothing about it; the gap
            // list used to report such groups as declaring_type_unreachable even while they were callable (owner
            // 2026-11-14: PathMap, Languages and DocumentTypeManager read that way).
            bool allStatic = e.Details.Where(d => d.Kind == "overload").All(d => d.Static);
            bool receiver = allStatic || ObjectType(type, input.Kinds) || model.Reachable.Contains(type)
                || input.Services.ContainsKey(type) || entryReachable.Contains(type);
            string? reason = HandleRestriction(type) == "masterdata_copy_pending" ? "masterdata_copy_pending" : !receiver ? "declaring_type_unreachable" : null;
            reason ??= e.Row.Kind is not ("property" or "method") ? "member_kind_not_dispatched" : e.Details.Where(d => d.Kind == "overload").Select(d => Exclusion(e, d, input.Enums, expanded, model.Reachable.Contains(type) ? null : input.Services)).FirstOrDefault(r => r is not null) ?? "unsupported_property_or_ambiguous_shape";
            return (e, reason, receiver ? "No unique supported compiled member shape" : "No approved return chain within depth 4; see handle reachability table");
        })];
    }
    private static string LegacyChainGapReport(string root, bool before = false) => "group\tnamespace\ttype\tmember_kind\treason\tdetail\n" + string.Join("", ChainGaps(root, before).Select(g => g.Entry.Row.Group + "\t" + g.Entry.Namespace + "\t" + g.Entry.Type + "\t" + g.Entry.Row.Kind + "\t" + g.Reason + "\t" + g.Detail + "\n"));
    private static string LegacyChainReachabilityReport(string root)
    {
        var input = ChainInputs(root);
        ChainAnalysis analysis = AnalyzeChains(input.Data, input.Types, input.Kinds, input.Enums, input.Services);
        JsonElement[] fields = ChainFields(root);
        var before = ChainGaps(root, true).Where(g => g.Reason is "declaring_type_unreachable" or "masterdata_copy_pending").GroupBy(g => g.Entry.Type, StringComparer.Ordinal);
        return "type\tbefore_unreachable_groups\tdepth\tapproved_source\tpossible_sources_and_blockers\n" + string.Join("", before.OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            string type = g.Key[2..];
            string sources = string.Join("; ", input.Data.Entries.SelectMany(e => e.Details.Where(d => d.Kind == "overload" && (d.ReturnType == type || ElementType(d.ReturnType, analysis.Model.Types) == type)).Select(d => (Entry: e, Member: d)))
                .Select(s => s.Member.Id + (s.Member.Static ? " [static factory]" : s.Member.ReturnType == type ? " [" + s.Entry.Row.Kind + "]" : " [collection element]") + (analysis.Model.Members.Any(m => m.Member.Id == s.Member.Id) ? " [approved]" : " [blocked: " + (HandleRestriction(type) ?? s.Entry.Row.ExcludedReason ?? s.Member.ExcludedReason ?? "receiver/signature/lifecycle restriction") + "]")));
            sources = string.Join("; ", new[] { sources }.Where(s => s.Length > 0).Concat(fields.Where(f => f.TryGetProperty("returns", out JsonElement r) && (r.GetString() == type || ElementType(r.GetString(), analysis.Model.Types) == type)).Select(f => f.GetProperty("id").GetString() + " [type-layer field; outside operation dispatch]")));
            bool reached = analysis.Paths.TryGetValue(type, out var path);
            return g.Key + "\t" + g.Count().ToString(System.Globalization.CultureInfo.InvariantCulture) + "\t" + (reached ? path.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-") + "\t" + (reached ? path.Source : HandleRestriction(type) ?? "none: no approved root/return chain") + "\t" + (sources.Length == 0 ? "none in public operation catalog (constructor/callback is not a trial root)" : sources) + "\n";
        }));
    }
}
