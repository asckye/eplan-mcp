using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage;

internal static partial class ApiTryGenerator
{
    private const string FactoryTargetCondition = " || group.EndsWith(\".#ctor+Create\", System.StringComparison.Ordinal)";
    internal static bool Factory(ApiEntry entry) => entry.Name == "#ctor+Create";
    internal static bool BannedFactory(ApiEntry entry, ApiDetail member, HashSet<string> banned) => Factory(entry) && member.Note == "Create" && banned.Contains("M:" + entry.Type[2..] + ".#ctor");
    private static readonly string[] _lifecyclePrefixes = ["Create", "Remove", "Delete", "Erase"];
    private static readonly string[] _sessionPrefixes = ["Destroy", "Init", "init", "Freeze", "Lock"];
    internal static bool Lifecycle(ApiEntry entry) => Factory(entry) || _lifecyclePrefixes.Any(p => entry.Name.StartsWith(p, StringComparison.Ordinal));
    internal static string? LifecycleExclusion(ApiEntry entry, ApiDetail member)
    {
        if (_sessionPrefixes.Any(p => entry.Name.StartsWith(p, StringComparison.Ordinal)))
        { return "lifecycle_or_creation"; }
        if (!Lifecycle(entry))
        { return null; }
        if (entry.Type == "T:Eplan.EplApi.DataModel.Project" && entry.Name == "Remove")
        { return "external_destination"; }
        if (entry.Name.Contains("Transient", StringComparison.Ordinal) || member.Parameters!.Any(p => p.Name.Contains("Transient", StringComparison.OrdinalIgnoreCase)))
        { return "transient_creation"; }
        // DataModelu.xml: strContureName is a full *.fc1 path despite its name.
        if (member.Parameters!.Any(p => p.Name == "strContureName"))
        { return "external_path_parameter"; }
        if (ApiTryServices.Exclusion(entry, member) is { } reason)
        { return reason; }
        return member.Parameters!.Any(p => Regex.IsMatch(p.Name + " " + p.Type, "article|partNr|partNumber|symbol|functiondefinition|segmentdefinition", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            || entry.Type.Contains(".MasterData.", StringComparison.Ordinal) ? "masterdata_or_parts_management" : null;
    }
    internal static ApiEntry[] Factories(ApiIndexData data, JsonElement[] constructors, JsonElement[] types)
    {
        bool Public(JsonElement value) => !value.TryGetProperty("access", out _) && !value.TryGetProperty("excluded", out _) && (!value.TryGetProperty("static", out JsonElement s) || !s.GetBoolean());
        var result = new List<ApiEntry>();
        foreach (JsonElement type in types.Where(t => Public(t) && t.GetProperty("kind").GetString() == "class" && (!t.TryGetProperty("modifiers", out JsonElement m) || !m.GetString()!.Contains("abstract", StringComparison.Ordinal))))
        {
            string id = type.GetProperty("id").GetString()!;
            JsonElement[] ctors = [.. constructors.Where(c => c.GetProperty("type").GetString() == id && Public(c))];
            ApiEntry? create = data.Entries.FirstOrDefault(e => e.Type == id && e.Name == "Create" && e.Row.ExcludedReason is null);
            bool empty = ctors.Any(c => !c.TryGetProperty("params", out JsonElement p) || p.GetArrayLength() == 0);
            string group = "O:" + id[2..] + ".#ctor+Create";
            // The source member ID keeps catalog and banned-symbol exclusions effective for the combined call.
            var details = (empty && create is not null ? create.Details : []).Where(d => d.Kind == "overload" && !d.Static).Select(d => d with
            { Signature = d.Signature![..d.Signature!.LastIndexOf(" : ", StringComparison.Ordinal)].Replace(".Create(", ".#ctor+Create(", StringComparison.Ordinal) + " : " + id[2..], ReturnType = id[2..], Note = "Create" }).ToList();
            foreach (JsonElement ctor in ctors.Where(c => c.TryGetProperty("params", out JsonElement p) && p.GetArrayLength() > 0))
            {
                ApiParameter[] parameters = JsonSerializer.Deserialize<ApiParameter[]>(ctor.GetProperty("params").GetRawText(), CoverageJson.Options)!;
                var detail = new ApiDetail("overload", ctor.GetProperty("id").GetString()!, id[2..] + ".#ctor+Create(" + string.Join(", ", parameters.Select(p => p.Type + " " + p.Name)) + ") : " + id[2..], Parameters: parameters, ReturnType: id[2..], Note: "#ctor");
                if (!details.Any(d => Names(d) == Names(detail)))
                { details.Add(detail); }
            }
            if (details.Count > 0)
            { result.Add(new(new(details[0].Id, group, "method", details[0].Signature!, "registered", null, [], false), type.GetProperty("ns").GetString()!, id, "#ctor+Create", null, [group], [.. details])); }
        }
        return [.. result];
    }
    internal static ApiIndexData WithLifecycleFactories(string root, ApiIndexData data)
    {
        string directory = Path.Combine(root, "catalog/eplan-2026.0.3");
        JsonElement[] types = [.. File.ReadLines(Path.Combine(directory, "types.jsonl")).Select(l => JsonSerializer.Deserialize<JsonElement>(l))];
        JsonElement[] constructors = [.. Directory.GetFiles(Path.Combine(directory, "members"), "Eplan.EplApi.DataModel*.jsonl").SelectMany(File.ReadLines)
            .Select(l => JsonSerializer.Deserialize<JsonElement>(l)).Where(m => m.GetProperty("kind").GetString() == "constructor")];
        var supported = ObjectKindGenerator.Build(types, data).Where(k => k.Reason is null).Select(k => "T:" + k.Type).ToHashSet(StringComparer.Ordinal);
        return data with { Entries = [.. data.Entries, .. Factories(data, constructors, types.Where(t => supported.Contains(t.GetProperty("id").GetString()!)).ToArray())] };
    }
    internal static string FactoryDispatch(ApiEntry entry, ApiDetail member, Func<ApiParameter, string> argument) =>
        "            case " + Literal(member.Signature!) + ": { " + (member.Note == "#ctor" ? "return new global::" + entry.Type[2..] + "(" : "var created = new global::" + entry.Type[2..] + "(); created.Create(")
        + string.Join(", ", member.Parameters!.Select(argument)) + ");" + (member.Note == "#ctor" ? "" : " return created;") + " }";
    private static readonly string[] _resourceNames = ["GetRecordNames", "GetVariableNames", "AssignedObjects", "SubPlacements", "SubPlacement3Ds", "ReferencedPlacements", "Connections", "ConnectionPoints", "FunctionTemplates", "PropertiesConfigurations", "LinkedSegments", "DaisyChains"];
    internal static string? ResourceProjection(string? type, Dictionary<string, string[]> kinds) => type switch
    {
        "System.Collections.Specialized.StringCollection" => ".Cast<string>().ToArray()",
        "Eplan.EplApi.DataModel.NetDefinitionPoint.NetConnectionPoint[]" => ".Select(o => new { o.Name, o.IdForNetBasedConnections }).ToArray()",
        "Eplan.EplApi.DataModel.Planning.PropertyConfiguration[]" => ".Select(o => o.Id.ToString()).ToArray()",
        _ => type?.EndsWith("[]", StringComparison.Ordinal) == true && ObjectType(type[..^2], kinds) ? ".Select(o => new { type = o.GetType().FullName, oid = o.ObjectIdentifier }).ToArray()" : null,
    };
    internal static string LifecycleReadback(ApiIndexData data, Dictionary<string, string[]> kinds)
    {
        IEnumerable<string> cases = data.Entries.Where(e => ObjectType(e.Type[2..], kinds) && e.Row.ExcludedReason is null && _resourceNames.Contains(e.Name, StringComparer.Ordinal))
            .SelectMany(e => e.Details.Where(d => d.Kind == "overload" && d.Access is null && !d.Static && d.ExcludedReason is null && d.Parameters is { Length: 0 }
                && (e.Row.Kind == "method" || d.Accessors?.Split(';').Contains("get", StringComparer.Ordinal) == true)).Select(d => (e, d)))
            .Where(s => ResourceProjection(s.d.ReturnType, kinds) is not null)
            .Select((s, i) =>
            {
                string receiver = "r" + i, value = receiver + "." + s.e.Name + (s.e.Row.Kind == "method" ? "()" : "");
                value += ResourceProjection(s.d.ReturnType, kinds);
                return "        if (target is global::" + s.e.Type[2..] + " " + receiver + ") { state[" + Literal(s.e.Row.Group) + "] = Newtonsoft.Json.JsonConvert.SerializeObject(" + value + "); }";
            });
        return "    internal static System.Collections.Generic.Dictionary<string, string> LifecycleReadback(StorableObject target)\n    {\n        var state = new System.Collections.Generic.Dictionary<string, string>();\n"
            + string.Join("\n", cases) + "\n        return state;\n    }\n";
    }
}
