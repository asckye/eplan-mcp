using System.Text.Json;

namespace EplanMcp.Coverage.Reporting;

/// <summary>
/// One counting unit of the coverage design (docs/design/2026-10-21-api-capability-coverage.md section 3): an overload
/// group of the operation layer, an object type of the data dictionary, or a row of the action catalog (an action, or
/// one TYPE of it). <see cref="Scope"/> is the namespace, the object type or the action name; <see cref="Kind"/> is
/// method or property for a group, the TYPE for an action row (null without one). A group is undocumented when none of
/// its counted overloads is in EPLAN's IntelliSense files.
/// </summary>
internal sealed record Unit(string Key, string Layer, string Scope, string? Type, string? Kind, string? Exclusion, bool Documented = true);

/// <summary>
/// The catalog of one EPLAN version, catalog/eplan-&lt;version&gt;/: the SDK part as the catalog extractor writes it
/// (types.jsonl, members/&lt;namespace&gt;.jsonl, data-dictionary.jsonl) and the action catalog (actions.json). Either part
/// may be missing; the report says so instead of guessing. Unknown fields are ignored.
/// </summary>
internal sealed class ApiCatalog
{
    public const string Operation = "operation";
    public const string DataDictionary = "data_dictionary";
    public const string Action = "action";

    private readonly Dictionary<string, string> _memberUnits = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dependencies = new(StringComparer.Ordinal);

    // Action names are case-insensitive in EPLAN; the Bridge may spell one differently from the catalog.
    private readonly Dictionary<string, string> _actions = new(StringComparer.OrdinalIgnoreCase);

    public bool HasSdk { get; private set; }

    public bool HasActions { get; private set; }

    public Dictionary<string, Unit> Units { get; } = new(StringComparer.Ordinal);

    /// <summary>The members of each overload group with whether the catalog excludes them, to tell a partly used group.</summary>
    public Dictionary<string, List<(string Id, bool Excluded)>> Groups { get; } = new(StringComparer.Ordinal);

    /// <summary>Property IDs (Object.NAME) and whether each is obsolete; the unit is the object type.</summary>
    public Dictionary<string, bool> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>Types and type-layer members (constructors, lifecycle, fields, constants, events, enum values): dependencies only.</summary>
    public int Dependencies => _dependencies.Count;

    public static ApiCatalog Load(string directory)
    {
        var catalog = new ApiCatalog();
        string types = Path.Combine(directory, "types.jsonl");
        if (File.Exists(types))
        {
            catalog.HasSdk = true;
            foreach (JsonElement type in Lines(types))
            {
                string id = type.GetProperty("id").GetString()!;
                catalog._dependencies.Add(id);
                if (type.TryGetProperty("values", out JsonElement values))
                {
                    catalog._dependencies.UnionWith(values.EnumerateObject().Select(v => $"F:{id[2..]}.{v.Name}"));
                }
            }

            foreach (string file in Directory.EnumerateFiles(Path.Combine(directory, "members"), "*.jsonl").Order(StringComparer.Ordinal))
            {
                foreach (JsonElement member in Lines(file))
                {
                    catalog.AddMember(member, Path.GetFileNameWithoutExtension(file));
                }
            }

            foreach (IGrouping<string, JsonElement> type in Lines(Path.Combine(directory, "data-dictionary.jsonl")).GroupBy(p => p.GetProperty("object").GetString()!))
            {
                foreach (JsonElement property in type)
                {
                    catalog.Properties[$"{type.Key}.{property.GetProperty("name").GetString()}"] = Flag(property, "obsolete");
                }

                // An object type counts unless every property ID of it is obsolete.
                catalog.Units["D:" + type.Key] = new Unit("D:" + type.Key, DataDictionary, type.Key, null, null, type.All(p => Flag(p, "obsolete")) ? "deprecated" : null);
            }
        }

        string actions = Path.Combine(directory, "actions.json");
        if (File.Exists(actions))
        {
            catalog.HasActions = true;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(actions));
            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                string id = row.GetProperty("id").GetString()!;
                string? type = row.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                string? excluded = row.GetProperty("status").GetString() == "excluded" ? row.GetProperty("reason").GetString() : null;
                catalog.Units[id] = new Unit(id, Action, row.GetProperty("action").GetString()!, null, type, excluded);
                catalog._actions[id] = id;
            }
        }

        return catalog;
    }

    /// <summary>
    /// The unit a Bridge-used ID counts for: its overload group; the object type of a property ID
    /// (<c>P:…&lt;Object&gt;PropertyList.NAME</c>, its indexed overload, <c>F:….Properties.&lt;Object&gt;.NAME</c>) or of the
    /// <c>&lt;Object&gt;PropertyList</c> type, which is that object's generic property path; its action row; "dependency" for a
    /// type-layer ID; null when the catalog does not have it.
    /// </summary>
    public string? Resolve(string id)
    {
        if (_memberUnits.TryGetValue(id, out string? group))
        {
            return group;
        }

        string[] parts = id[2..].Split('(')[0].Split('.');
        string? list = id[0] == 'T' && parts[^1].EndsWith("PropertyList", StringComparison.Ordinal) ? "D:" + parts[^1][..^"PropertyList".Length] : null;
        return PropertyOf(id) is { } property ? "D:" + property.Split('.')[0]
            : list is not null && Units.ContainsKey(list) ? list
            : _dependencies.Contains(id) ? "dependency"
            : _actions.TryGetValue(id, out string? action) ? action
            : null;
    }

    /// <summary>The property ID (Object.NAME) of <c>P:…&lt;Object&gt;PropertyList.NAME</c>, its indexed overload or <c>F:….Properties.&lt;Object&gt;.NAME</c>.</summary>
    public string? PropertyOf(string id)
    {
        string[] parts = id[2..].Split('(')[0].Split('.');
        string? property =
            id[0] == 'P' && parts.Length > 2 && parts[^2].EndsWith("PropertyList", StringComparison.Ordinal) ? $"{parts[^2][..^"PropertyList".Length]}.{parts[^1]}"
            : id[0] == 'F' && parts.Length > 3 && parts[^3] == "Properties" ? $"{parts[^2]}.{parts[^1]}"
            : null;
        return property is not null && Properties.ContainsKey(property) ? property : null;
    }

    /// <summary>Whether the action catalog knows the action of an <c>A:name</c> the Bridge uses without a TYPE.</summary>
    public bool HasAction(string id) => Units.Values.Any(u => u.Layer == Action && string.Equals("A:" + u.Scope, id, StringComparison.OrdinalIgnoreCase));

    private static bool Flag(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    /// <summary>The JSON object on each non-empty line of a catalog <c>.jsonl</c> file.</summary>
    internal static IEnumerable<JsonElement> Lines(string path) =>
        File.ReadLines(path).Where(l => l.Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone());

    private void AddMember(JsonElement member, string ns)
    {
        string id = member.GetProperty("id").GetString()!;
        if (member.GetProperty("layer").GetString() != Operation)
        {
            _dependencies.Add(id);
            return;
        }

        string group = member.TryGetProperty("group", out JsonElement g) ? g.GetString()! : id;
        string? exclusion = member.TryGetProperty("excluded", out JsonElement e) ? e.GetProperty("reason").GetString() : null;
        bool documented = exclusion is null && !(member.TryGetProperty("documented", out JsonElement d) && d.ValueKind == JsonValueKind.False);
        _memberUnits[id] = group;
        (Groups.TryGetValue(group, out var members) ? members : Groups[group] = []).Add((id, exclusion is not null));

        // A group counts unless every overload in it is excluded.
        Units[group] = Units.TryGetValue(group, out Unit? known)
            ? known with { Exclusion = known.Exclusion is null ? null : exclusion, Documented = known.Documented || documented }
            : new Unit(group, Operation, ns, member.TryGetProperty("type", out JsonElement t) ? t.GetString() : null, member.GetProperty("kind").GetString(), exclusion, documented);
    }
}
