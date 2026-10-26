using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tools;

internal static class PropertySetRequest
{
    private static readonly JsonSerializerOptions _json = new(PropertySchemaTools.MetadataJson)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static PropertySetArgs Parse(IDictionary<string, JsonElement>? input)
    {
        List<string> violations = UnknownArgumentFilter.Violations(PropertySetContract.Input, input);
        JsonObject root = JsonSerializer.SerializeToNode(input, HostJson.Options) as JsonObject ?? [];
        if (Encoding.UTF8.GetByteCount(root.ToJsonString(HostJson.Options)) > 64 * 1024
            || root["project"] is not JsonValue project || !project.TryGetValue<string>(out string? name) || string.IsNullOrWhiteSpace(name)
            || root.ContainsKey("dry_run") && root["dry_run"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)
            || root.ContainsKey("response_format") && root["response_format"]?.ToJsonString() is not ("\"concise\"" or "\"detailed\""))
        {
            violations.Add("Use a nonempty project, boolean dry_run, concise/detailed response_format and at most 64 KiB of JSON.");
        }
        var args = new PropertySetArgs();
        var denied = new List<string>();
        JsonArray changes = root["changes"] as JsonArray ?? [];
        if (changes.Count is < 1 or > 100)
        {
            violations.Add("changes must contain 1..100 assignments.");
        }
        foreach ((JsonNode? item, int i) in changes.Select((c, i) => (c, i)))
        {
            string at = "/changes/" + i.ToString(CultureInfo.InvariantCulture);
            try
            {
                JsonObject change = item?.DeepClone() as JsonObject ?? throw new JsonException();
                if (change.Any(p => p.Value is null) || change["property"]?.AsObject().Any(p => p.Value is null) == true
                    || change["target"]?.AsObject().Any(p => p.Value is null) == true)
                {
                    throw new JsonException();
                }
                string? text = change["value"]?.GetValue<string>();
                change["value"] = null;
                PropertySetChange parsed = change.Deserialize<PropertySetChange>(_json)!;
                PropertyTarget target = parsed.Target ?? throw new JsonException();
                bool function = target.DeviceTag is not null || target.Locator?.Kind == "function";
                PropertySelector selector = parsed.Property ?? throw new JsonException();
                PropertyDictionaryEntry? definition = selector.PropertyId is { } id ? PropertyCatalog.Find("Function", id)
                    : PropertyCatalog.Find("Function", selector.PropertyName!);
                if (definition is null)
                {
                    violations.Add(at + "/property: use a known ID or exact formal name; consult eplan_property_schema.");
                    continue;
                }
                PropertyWriteEntry? entry = PropertyWriteWhitelist.Entries.Values.SingleOrDefault(e => e.EplanMajorVersion == 2026
                    && e.ObjectType == "Function" && e.PropertyId == definition.PropertyId);
                if (!function || entry is null)
                {
                    denied.Add(at + ": target/property is outside the shipped B1 whitelist.");
                    continue;
                }
                if (text is null || text.Length > 500 || selector.Index is not null
                    || entry.PropertyId == 20011 && (parsed.Language is not { Length: 5 } language || language[2] != '_'
                        || language[..2].Any(c => c is < 'a' or > 'z') || language[3..].Any(c => c is < 'A' or > 'Z'))
                    || entry.PropertyId == 20027 && parsed.Language is not null)
                {
                    violations.Add(at + ": supply a string up to 500 characters, no index; 20011 requires language, 20027 forbids it.");
                }
                parsed.Value = new PropertyTypedValue(entry.Definition.ValueType!, text: text);
                args.Changes.Add(parsed);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            {
                violations.Add(at + ": use the published target/selector shape and a literal string value; unknown fields are refused.");
            }
        }
        if (args.Changes.Select(c => JsonSerializer.Serialize(c.Target, _json)).Distinct(StringComparer.Ordinal).Count() > 50)
        {
            violations.Add("changes may address at most 50 objects.");
        }
        if (violations.Count + denied.Count > 0)
        {
            BridgeError error = ToolResults.InvalidArgumentError([.. violations, .. denied]);
            error.Reason = denied.Count > 0 ? ErrorReason.PolicyDenied : ErrorReason.InvalidArgument;
            throw new BridgeCallException(error);
        }
        args.Project = root["project"]!.GetValue<string>();
        return args;
    }
}
