using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

internal static class ApiCallTemplates
{
    internal static JsonObject[] For(ApiEntry entry)
    {
        string group = entry.Row.Group;
        if (entry.Row.ExcludedReason is null && ApiTryCatalog.Allowed(group))
        {
            JsonObject[] methods = [.. ApiTryCatalog.Methods(group).SelectMany(s => Templates(group, s, ApiTryCatalog.MethodAccess(group) ?? "read", false))];
            if (methods.Length > 0 && (entry.Row.Kind != "property" || ApiTryCatalog.Settings(group)))
            { return methods; }
            if (ApiTryCatalog.Shape(group) is { } shape)
            { return ApiTryCatalog.ValueShape(group) is null ? [.. methods, .. Templates(group, shape, "read", false)] : [.. methods, .. Templates(group, shape, "read", false), .. Templates(group, shape, "write", true)]; }
            if (methods.Length > 0)
            { return methods; }
        }
        return [new() { ["dispatchable"] = false, ["exclusion_reason"] = ApiTryCatalog.ExclusionReason(group) ?? entry.Row.ExcludedReason ?? "No compiled signature in the generated dispatch catalog.",
            ["alternatives"] = JsonSerializer.SerializeToNode(entry.Row.Tools) }];
    }

    private static IEnumerable<JsonObject> Templates(string group, string[] shape, string access, bool setter)
    {
        if (ApiTryCatalog.Refused(group, access) is not null || !setter && ApiTryCatalog.MethodAccess(group) is not null && ApiTryCatalog.Refused(group, "call") is not null)
        {
            yield return new()
            {
                ["signature"] = shape[1],
                ["access"] = access,
                ["dispatchable"] = false,
                ["refusal_reason"] = "refused_unsafe",
                ["reason"] = ErrorReason.CapabilityMissing,
                ["exclusion_reason"] = ApiTryCatalog.RefusalMessage
            };
            yield break;
        }
        if (ApiTryCatalog.SettingsMethods(group).Any(s => s.SequenceEqual(shape)))
        {
            yield return Create(group, shape, access, setter, receiver: ["Eplan.EplApi.DataModel.Project"]);
            if (!ApiTryCatalog.Entry(shape[1]) || shape.Skip(2).Contains("node", StringComparer.Ordinal))
            { yield break; }
        }
        if (ApiTryCatalog.ReceiverShape(shape[1]) is ["via", var type])
        {
            foreach (string[] path in ApiTryCatalog.ViaPaths(type))
            { yield return Create(group, shape, access, setter, path); }
            if (ApiTryCatalog.HandleType(type))
            { yield return Create(group, shape, access, setter); }
        }
        else
        { yield return Create(group, shape, access, setter); }
    }

    private static JsonObject Create(string group, string[] shape, string access, bool setter, string[]? path = null, string[]? receiver = null)
    {
        var parameters = new JsonObject();
        var example = new JsonObject();
        if ((receiver ?? ApiTryCatalog.ReceiverShape(shape[1])) is { } receiverShape)
        {
            var target = Parameter(path is not null ? [path[0]] : receiverShape is ["via", var type] ? ["handle", type] : receiverShape);
            parameters["target"] = target.Description;
            example["target"] = target.Example;
            if (path is not null)
            {
                parameters["via"] = new JsonObject { ["kind"] = "string", ["required"] = true, ["const"] = path[1], ["receiver_type"] = receiverShape[1], ["source"] = "generated public property path from target" };
                example["via"] = path[1];
                if (ApiTryCatalog.ViaKey(shape[0], path[1]))
                {
                    var key = Parameter(["System.String"]);
                    key.Description["source"] = "layer name in the target project";
                    parameters["via_key"] = key.Description;
                    example["via_key"] = "<layer name>";
                }
            }
        }
        foreach (string name in shape.Skip(2).Concat(setter ? ["value"] : Array.Empty<string>()))
        {
            string[] type = (name == "value" && setter ? ApiTryCatalog.ValueShape(group) : ApiTryCatalog.ParameterShape(shape[1], name))!;
            (JsonObject description, JsonNode value) = Parameter(type);
            parameters[name] = description;
            example[name] = value;
        }
        if (ApiTryCatalog.Settings(group) && example.ContainsKey("node"))
        {
            example["node"] = group.Contains(".ProjectSetting", StringComparison.Ordinal) ? "PROJECT" : "USER";
            parameters["node"]!["source"] = "enumerated settings node under USER/STATION/PROJECT";
        }
        return new()
        {
            ["tool"] = ApiTryTools.Tool,
            ["signature"] = shape[1],
            ["access"] = access,
            ["dispatchable"] = true,
            ["arguments"] = new JsonObject
            {
                ["group"] = new JsonObject { ["kind"] = "string", ["required"] = true, ["const"] = group },
                ["project"] = new JsonObject { ["kind"] = "string", ["required"] = true },
                ["parameters"] = new JsonObject { ["required"] = true, ["kind"] = "object", ["properties"] = parameters },
                ["dry_run"] = new JsonObject { ["kind"] = "bool", ["required"] = false, ["default"] = WriteToolDefaults.Preview }
            },
            ["example"] = new JsonObject { ["group"] = group, ["project"] = "<scratch project>", ["parameters"] = example, ["dry_run"] = true },
            ["note"] = "Replace complete locator/handle/path placeholders with fresh confined values. Requires full profile and apiTry:on. Omit/true previews; false executes with per-call approval."
        };
    }

    internal static (JsonObject Description, JsonNode Example) Parameter(string[] shape)
    {
        string type = shape[0];
        var description = new JsonObject { ["required"] = true };
        JsonNode value;
        if (type == "enum")
        { description["allowed_values"] = JsonSerializer.SerializeToNode(shape.Skip(1)); value = JsonValue.Create(shape[1])!; type = "enum"; }
        else if (type == "array" || type.EndsWith("[]", StringComparison.Ordinal))
        {
            var item = Parameter(type == "array" ? shape[1..] : [type[..^2]]);
            description["items"] = item.Description;
            value = new JsonArray(item.Example);
            type = "array";
        }
        else if (type == "handle" || type.StartsWith("handle:", StringComparison.Ordinal) || type != "Eplan.EplApi.Base.PointD" && ApiTryCatalog.Kinds(type).Length == 0 && ApiTryCatalog.HandleType(type))
        {
            string handleType = type == "handle" ? shape[1] : type.StartsWith("handle:", StringComparison.Ordinal) ? type[7..] : type;
            description["type"] = handleType;
            description["source"] = "handle returned by " + string.Join(", ", shape.Skip(type == "handle" ? 2 : 1).Concat(ApiTryCatalog.Producers(handleType)).Distinct());
            value = new JsonObject { ["handle"] = "h:" + new string('a', 32), ["type"] = handleType };
            type = "handle";
        }
        else if (type is "input_path" or "output_path")
        { description["confinement"] = type == "input_path" ? "configured import roots" : "configured export root"; value = JsonValue.Create("<confined " + type + ">")!; }
        else if (ApiTryCatalog.Kinds(type) is { Length: > 0 } kinds)
        { return (Locator(kinds.Contains(type.Split('.').Last().ToLowerInvariant(), StringComparer.Ordinal) ? type.Split('.').Last().ToLowerInvariant() : kinds[0]), LocatorExample()); }
        else
        {
            type = type switch { "System.String" => "string", "System.Boolean" => "bool", "System.Int16" or "System.UInt32" or "System.Int32" or "System.Int64" => "int", "System.Double" => "double", "Eplan.EplApi.Base.PointD" => "PointD", _ => throw new InvalidOperationException("Unknown generated parameter kind: " + type) };
            value = type switch { "bool" => JsonValue.Create(false)!, "int" => JsonValue.Create(1)!, "double" => JsonValue.Create(1.0)!, "PointD" => new JsonObject { ["x"] = 0, ["y"] = 0 }, _ => JsonValue.Create("<string>")! };
        }
        description["kind"] = type;
        return (description, value);
    }

    private static JsonObject Locator(string kind) => new()
    {
        ["kind"] = "locator",
        ["object_type"] = kind,
        ["required"] = true,
        ["source"] = new JsonObject { ["tool"] = "eplan_object_find", ["arguments"] = new JsonObject { ["project"] = "<scratch project>", ["object_type"] = kind } }
    };
    private static JsonObject LocatorExample() => new() { ["project"] = "<scratch project>", ["page"] = "<page from discovery>", ["type"] = 1, ["oid"] = 1, ["digest"] = new string('a', 64) };

    internal static JsonObject? Closest(ApiIndex index, string group, JsonElement given) => index.Data.Entries.FirstOrDefault(e => e.Row.Group == group) is { } entry
        ? For(entry).OrderBy(t => Distance(t["example"]?["parameters"] as JsonObject, given)).FirstOrDefault() : null;
    internal static int Distance(JsonObject? expected, JsonElement given)
    {
        string[] names = given.ValueKind == JsonValueKind.Object ? [.. given.EnumerateObject().Select(p => p.Name)] : [];
        return expected is null ? int.MaxValue : expected.Count(p => !names.Contains(p.Key, StringComparer.Ordinal)) + names.Count(n => !expected.ContainsKey(n));
    }
}
