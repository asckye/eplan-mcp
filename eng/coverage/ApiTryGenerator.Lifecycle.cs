using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage;

internal static partial class ApiTryGenerator
{
    internal static bool Factory(ApiDetail member) => member.Note == "Create";
    internal static bool BannedFactory(ApiEntry entry, ApiDetail member, HashSet<string> banned) => member.Note == "Create" && banned.Contains("M:" + entry.Type[2..] + ".#ctor");
    private static readonly string[] _lifecyclePrefixes = ["Create", "Remove", "Delete", "Erase"];
    private static readonly string[] _sessionPrefixes = ["Destroy", "Init", "init", "Freeze", "Lock"];
    internal static bool Lifecycle(ApiEntry entry) => _lifecyclePrefixes.Any(p => entry.Name.StartsWith(p, StringComparison.Ordinal));
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
            bool empty = ctors.Any(c => !c.TryGetProperty("params", out JsonElement p) || p.GetArrayLength() == 0);
            foreach (ApiEntry entry in data.Entries.Where(e => empty && e.Type == id && e.Row.ExcludedReason is null && e.Name == "Create"))
            {
                // Keep the native group, member ID and signature; only dispatch returns the created receiver.
                ApiDetail[] details = [.. entry.Details.Select(d => d.Kind == "overload" && !d.Static
                    ? d with { ReturnType = id[2..], Note = entry.Name } : d)];
                if (details.Any(Factory))
                { result.Add(entry with { Details = details }); }
            }
        }
        return [.. result];
    }
    internal static ApiIndexData WithLifecycleFactories(string root, ApiIndexData data)
    {
        string directory = Path.Combine(root, "catalog/eplan-2026.0.3");
        if (!File.Exists(Path.Combine(directory, "types.jsonl")))
        { return data; }
        JsonElement[] types = [.. File.ReadLines(Path.Combine(directory, "types.jsonl")).Select(l => JsonSerializer.Deserialize<JsonElement>(l))];
        JsonElement[] constructors = [.. Directory.GetFiles(Path.Combine(directory, "members"), "Eplan.EplApi.DataModel*.jsonl").SelectMany(File.ReadLines)
            .Select(l => JsonSerializer.Deserialize<JsonElement>(l)).Where(m => m.GetProperty("kind").GetString() == "constructor")];
        // Selection below still requires a discovery kind or an independently reachable handle; no new roots.
        var factories = Factories(data, constructors, types).ToDictionary(e => e.Row.Group, StringComparer.Ordinal);
        return data with
        {
            Entries = [.. data.Entries.Select(e => factories.GetValueOrDefault(e.Row.Group)
            ?? (e.Name == "Create" ? e with { Details = [.. e.Details.Select(d => d.Kind == "overload" && !d.Static ? d with { Note = "factory_constructor_unavailable" } : d)] } : e))]
        };
    }
    internal static string FactoryDispatch(ApiEntry entry, ApiDetail member, Func<ApiParameter, string> argument) =>
        "            case " + Literal(member.Signature!) + ": { var created = new global::" + entry.Type[2..] + "(); created.Create("
        + string.Join(", ", member.Parameters!.Select(argument)) + "); return created; }";
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
