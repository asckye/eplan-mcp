using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

/// <summary>Acceptance redraw comparison: unordered connections, structural names and coordinate tolerance.</summary>
internal static class ExpectedReview
{
    private static readonly string[] _coreFields = ["label", "colour"];
    internal static JsonObject Schema()
    {
        static JsonNode Type(string type) => JsonNode.Parse("{\"type\":\"" + type + "\"}")!;
        static JsonObject Obj(string required, params (string Key, JsonNode Value)[] fields) => new() { ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray(required.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["properties"] = new JsonObject(fields.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value))) };
        static JsonNode Array(JsonNode item) => new JsonObject { ["type"] = "array", ["maxItems"] = 500, ["items"] = item };
        static JsonNode Point() => JsonNode.Parse("""{"type":"array","minItems":2,"maxItems":2,"items":{"type":"number"}}""")!;
        JsonNode End() => Obj("device pin", ("device", Type("string")), ("pin", Type("string")));
        JsonNode Wire(bool core) => core ? Obj("from to label colour", ("from", End()), ("to", End()), ("label", Type("string")), ("colour", Type("string"))) : Obj("from to", ("from", End()), ("to", End()));
        return Obj("", ("devices", Array(Obj("tag position", ("tag", Type("string")), ("position", Point()), ("library", Type("string")), ("symbol", Type("string")), ("symbol_variant", Type("integer"))))), ("connections", Array(Wire(false))),
            ("cables", Array(Obj("designation cores", ("designation", Type("string")), ("cores", Array(Wire(true)))))), ("interruptions", Array(Obj("name position", ("name", Type("string")), ("position", Point()), ("counterpart", Obj("page position", ("page", Type("string")), ("position", Point())))))), ("texts", Array(Obj("text position", ("text", Type("string")), ("position", Point()), ("angle", Type("number"))))));
    }
    internal static void Validate(JsonNode? value, JsonNode schema, string path, List<string> errors)
    {
        string type = schema["type"]!.GetValue<string>();
        bool valid = type switch { "object" => value is JsonObject, "array" => value is JsonArray, "string" => value is JsonValue s && s.TryGetValue<string>(out _), "integer" => value is JsonValue i && i.TryGetValue<int>(out _), _ => value is JsonValue n && n.TryGetValue<double>(out double d) && double.IsFinite(d) };
        if (!valid)
        { errors.Add(path + ": Supply " + type + "."); return; }
        if (value is JsonObject obj)
        {
            foreach (string key in schema["required"]!.AsArray().Select(k => k!.GetValue<string>()))
            { if (!obj.ContainsKey(key)) { errors.Add(path + "/" + key + ": Required."); } }
            foreach (var field in obj)
            { if (schema["properties"]![field.Key] is { } child) { Validate(field.Value, child, path + "/" + field.Key, errors); } else { errors.Add(path + "/" + field.Key + ": Unknown member."); } }
        }
        if (value is JsonArray array)
        {
            if (array.Count > (schema["maxItems"]?.GetValue<int>() ?? 500) || array.Count < (schema["minItems"]?.GetValue<int>() ?? 0))
            { errors.Add(path + ": Array size is outside schema bounds."); }
            for (int i = 0; i < array.Count; i++)
            { Validate(array[i], schema["items"]!, path + "/" + i.ToString(CultureInfo.InvariantCulture), errors); }
        }
    }
    internal static bool Near(IEnumerable<double> expected, IEnumerable<double> actual, double tolerance) => expected.Count() == actual.Count() && expected.Zip(actual, (a, b) => Math.Abs(a - b) <= tolerance).All(v => v);
    internal static JsonArray Compare(JsonObject expected, JsonObject actual, string page, double tolerance)
    {
        var differences = new JsonArray();
        bool Equal(JsonNode? e, JsonNode? a, string key = "")
        {
            if (e is JsonObject obj)
            { return a is JsonObject && obj.Where(f => f.Key is not ("counterpart" or "cores")).All(f => Equal(f.Value, a[f.Key], f.Key)); }
            if (e is JsonArray arr)
            { return a is JsonArray other && Near(arr.Select(v => v!.GetValue<double>()), other.Select(v => v!.GetValue<double>()), tolerance); }
            if (e is JsonValue v && v.TryGetValue<string>(out string? text))
            { string? read = a?.ToString(); return key is "tag" or "device" or "designation" ? read is not null && DrawingNames.SameName(text, read, page) : text == read || key == "text" && "??_??@" + text + ";" == read; }
            return JsonNode.DeepEquals(e, a);
        }
        bool Ends(JsonNode e, JsonNode a) => Equal(e["from"], a["from"]) && Equal(e["to"], a["to"]) || Equal(e["from"], a["to"]) && Equal(e["to"], a["from"]);
        void Rows(string section, JsonArray targets, JsonArray observed, string prefix)
        {
            var remaining = observed.ToList();
            void Add(string kind, string path, JsonNode? target, JsonNode? read) => differences.Add(new JsonObject { ["kind"] = kind, ["path"] = path, ["target"] = target?.DeepClone(), ["actual"] = read?.DeepClone() });
            for (int i = 0; i < targets.Count; i++)
            {
                JsonNode target = targets[i]!;
                string path = prefix + "/" + i.ToString(CultureInfo.InvariantCulture);
                string identity = section switch { "devices" => "tag", "cables" => "designation", "interruptions" => "name", _ => "text" };
                JsonNode? read = remaining.FirstOrDefault(a => a is not null && (section is "connections" or "cores" ? Ends(target, a) : Equal(target[identity], a[identity], identity) && (section != "interruptions" || Equal(target["position"], a["position"])))) ?? (section is "texts" or "interruptions" ? remaining.FirstOrDefault(a => a is not null && Equal(target[identity], a[identity], identity)) : null);
                if (read is null)
                { Add("missing", path, target, null); continue; }
                remaining.Remove(read);
                bool equal = section is "connections" or "cores" ? Ends(target, read) && _coreFields.All(k => target[k] is null || Equal(target[k], read[k], k)) : Equal(target, read);
                if (!equal)
                { Add("mismatched", path, target, read); }
                if (section == "cables")
                { Rows("cores", target["cores"]!.AsArray(), read["cores"]!.AsArray(), path + "/cores"); }
            }
            foreach (JsonNode? read in remaining)
            { Add("extra", prefix, null, read); }
        }
        foreach (var section in expected)
        { Rows(section.Key, section.Value!.AsArray(), actual[section.Key]!.AsArray(), "/" + section.Key); }
        return differences;
    }
}
