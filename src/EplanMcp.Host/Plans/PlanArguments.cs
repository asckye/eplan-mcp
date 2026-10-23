using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Plans;

/// <summary>Pure input compilation. Defaults are supplied from resolved compilation context, never the later UI state.</summary>
internal static class PlanArguments
{
    public static PlanArgumentCheck Check(JsonElement request, string defaultLanguage, string defaultDeviceScheme)
    {
        var errors = new List<PlanViolation>();
        var steps = new List<PlanArgumentStep>();
        if (request.ValueKind != JsonValueKind.Object)
        { return new([], [new("", "The plan request must be an object.")]); }
        var root = JsonNode.Parse(request.GetRawText())!.AsObject();
        foreach (var p in root.Where(p => p.Key is not ("project" or "steps")))
        { errors.Add(new("/" + Escape(p.Key), "Unknown plan argument.")); }
        foreach (string message in ToolArguments.WriteProjectViolations(root["project"] is JsonValue v && v.TryGetValue(out string? project) ? project : null))
        { errors.Add(new("/project", message)); }
        if (root["steps"] is not JsonArray input)
        { errors.Add(new("/steps", "Provide an array of 1 to 20 steps.")); return new([], [.. errors]); }
        if (input.Count is < 1 or > 20)
        { errors.Add(new("/steps", "Provide 1 to 20 steps.")); }
        long bytes = 0, locators = 0;
        for (int i = 0; i < input.Count; i++)
        {
            string at = "/steps/" + i.ToString(CultureInfo.InvariantCulture);
            if (input[i] is not JsonObject step)
            { errors.Add(new(at, "A step must be an object.")); continue; }
            foreach (var p in step.Where(p => p.Key is not ("op" or "args" or "depends_on")))
            { errors.Add(new(at + "/" + Escape(p.Key), "Unknown step argument.")); }
            string op = step["op"] is JsonValue value && value.TryGetValue(out string? text) ? text : "";
            bool offered = PlanStepCatalog.Offers(op);
            if (!offered)
            { errors.Add(new(at + "/op", $"Operation '{op}' is not offered by workflow plans.")); }
            var dependencies = new List<int>();
            if (step.ContainsKey("depends_on") && step["depends_on"] is not JsonArray)
            { errors.Add(new(at + "/depends_on", "Dependencies must be an array of earlier step numbers.")); }
            foreach (var (dependency, j) in (step["depends_on"] as JsonArray ?? []).Select((n, j) => (n, j)))
            {
                if (dependency is not JsonValue d || !d.TryGetValue(out int k) || k < 1 || k > i || dependencies.Contains(k))
                { errors.Add(new(at + "/depends_on/" + j.ToString(CultureInfo.InvariantCulture), "Dependencies must be unique earlier step numbers; self and forward references are forbidden.")); }
                else
                { dependencies.Add(k); }
            }
            if (step["args"] is not JsonObject args)
            { errors.Add(new(at + "/args", "Step args must be an object.")); continue; }
            bytes += Encoding.UTF8.GetByteCount(request.GetProperty("steps")[i].GetProperty("args").GetRawText());
            locators += args["pages"] is JsonArray pages ? pages.Count : 1;
            if (!offered)
            { continue; }
            JsonObject schema = PlanStepCatalog.ArgsSchema(op);
            int before = errors.Count;
            CheckValue(args, schema, at + "/args", "", op, i, steps, dependencies, errors);
            foreach (string message in PlanStepCatalog.Violations(op, args))
            {
                string field = message.Split('\'').ElementAtOrDefault(1) ?? "";
                if (field == "start" && args["start"] is JsonValue start && start.TryGetValue(out int n) && n >= 1)
                { field = "step"; }
                errors.Add(new(at + "/args/" + Escape(field), message));
            }
            if (op == "macro.insert")
            {
                if (args["variant"] is JsonValue variant && variant.TryGetValue(out int variantNumber) && variantNumber != 0)
                { errors.Add(new(at + "/args/variant", "Plans offer only macro variant 0 (MultiLine).")); }
                if (args["numbering"]?.ToString() != "keep")
                { errors.Add(new(at + "/args/numbering", "Plans offer only numbering keep.")); }
                string macro = (args["macro"]?.ToString() ?? "").Replace('\\', '/');
                if (!macro.EndsWith(".ema", StringComparison.OrdinalIgnoreCase) || macro.Contains(':', StringComparison.Ordinal)
                    || macro.Split('/').Any(p => p is "" or "." or ".." || p.Contains('$', StringComparison.Ordinal)))
                { errors.Add(new(at + "/args/macro", "Use a relative .ema path under $(MD_MACROS), as eplan_macro_list returns it.")); }
            }
            if (op == "device.renumber" && args["pages"] is null)
            { errors.Add(new(at + "/args/pages", "device.renumber requires explicit pages (1 to 50).")); }
            if (errors.Count != before)
            { continue; }
            if (op is "page.create" or "page.update")
            {
                if (string.IsNullOrEmpty(args["language"]?.GetValue<string>()))
                { args["language"] = defaultLanguage; }
                if (op == "page.create")
                { foreach (JsonObject page in args["pages"]!.AsArray().Cast<JsonObject>()) { page["page_type"] ??= "Circuit"; page["description"] ??= null; } }
            }
            if (op == "device.renumber")
            {
                args["scheme"] = string.IsNullOrWhiteSpace(args["scheme"]?.GetValue<string>()) ? defaultDeviceScheme : args["scheme"]!.GetValue<string>().Trim();
                if (args["pages"] is null)
                { errors.Add(new(at + "/args/pages", "device.renumber requires explicit pages (1 to 50).")); }
            }
            steps.Add(new(i + 1, op, JsonSerializer.SerializeToElement(args), PlanStepCatalog.Tier(op), [.. dependencies.Order()]));
        }
        if (bytes > 256 * 1024)
        { errors.Add(new("/steps", "UTF-8 args JSON must not exceed 256 KiB.")); }
        if (locators > 200)
        { errors.Add(new("/steps", "Explicit page/device locators must not exceed 200.")); }
        return new([.. steps], [.. errors]);
    }

    private static void CheckValue(JsonNode? node, JsonObject schema, string at, string field, string op, int index,
        List<PlanArgumentStep> steps, List<int> dependencies, List<PlanViolation> errors)
    {
        if (node is JsonObject reference && PlanStepCatalog.IsPageSlot(op, field))
        {
            int k = reference["from_step"] is JsonValue number && number.TryGetValue(out int n) ? n : 0;
            string? name = reference["page"] is JsonValue value && value.TryGetValue(out string? s) ? s : null;
            PlanArgumentStep? source = steps.Find(s => s.Index == k && s.Op == "page.create");
            if (reference.Count != 2 || k < 1 || k > index || name is null || source is null
                || source.Args.GetProperty("pages").EnumerateArray().Count(p => string.Equals(p.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)) != 1
                || !source.Args.GetProperty("pages").EnumerateArray().Any(p => p.GetProperty("name").GetString() == name))
            { errors.Add(new(at, "A page reference must name a unique page from an earlier page.create step using only from_step and page.")); return; }
            if (!dependencies.Contains(k))
            { dependencies.Add(k); }
            node.ReplaceWith(JsonValue.Create(name));
            return;
        }
        string kind = node is JsonObject ? "object" : node is JsonArray ? "array" : node?.GetValueKind().ToString().ToLowerInvariant() ?? "null";
        string[] types = schema["type"] is JsonArray typesArray ? [.. typesArray.Select(t => t!.GetValue<string>())] : [schema["type"]!.GetValue<string>()];
        bool integer = node is JsonValue integerValue && integerValue.TryGetValue(out int _);
        if (!types.Contains(kind, StringComparer.Ordinal) && !(kind == "number" && types.Contains("integer", StringComparer.Ordinal) && integer)
            && !(kind is "true" or "false" && types.Contains("boolean", StringComparer.Ordinal)))
        { errors.Add(new(at, "Value must have type " + string.Join(" or ", types) + ".")); return; }
        if (schema["enum"] is JsonArray choices && node is not null && !choices.Any(c => JsonNode.DeepEquals(c, node)))
        { errors.Add(new(at, "Value must be one of " + choices.ToJsonString() + ".")); }
        if (node is JsonObject obj && schema["properties"] is JsonObject properties)
        {
            // Only this object's names here; recursion below handles nested objects and page references.
            JsonElement names = JsonSerializer.SerializeToElement(new { properties = properties.ToDictionary(p => p.Key, _ => new { }), required = schema["required"] });
            foreach (string message in UnknownArgumentFilter.Violations(names, obj.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value))))
            { errors.Add(new(at + "/" + Escape(message[(message.IndexOf('\'', StringComparison.Ordinal) + 1)..message.LastIndexOf('\'')]), message)); }
            foreach (var p in properties)
            {
                bool required = schema["required"]?.AsArray().Any(n => n!.GetValue<string>() == p.Key) == true;
                if (required && obj[p.Key] is null)
                { continue; }
                if (!obj.ContainsKey(p.Key))
                { obj[p.Key] = p.Key == "numbering" && op == "macro.insert" ? JsonValue.Create("keep") : p.Value!["default"]?.DeepClone(); }
                CheckValue(obj[p.Key], p.Value!.AsObject(), at + "/" + p.Key, p.Key, op, index, steps, dependencies, errors);
            }
        }
        if (node is JsonArray array && schema["items"] is JsonObject items)
        { for (int j = 0; j < array.Count; j++) { CheckValue(array[j], items, at + "/" + j.ToString(CultureInfo.InvariantCulture), field == "pages" ? "page_item" : "", op, index, steps, dependencies, errors); } }
    }

    private static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
