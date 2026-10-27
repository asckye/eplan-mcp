using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static EplanMcp.Packs.BindingReadModel;

namespace EplanMcp.Packs;

/// <summary>Closed, finite templates. Expansion does not grant capabilities, approve or execute a plan.</summary>
public static class WorkflowValidator
{
    private static readonly Dictionary<string, string> _shapes = new(StringComparer.Ordinal)
    {
        ["page.create"] = "pages:create[] language:text?",
        ["page.update"] = "pages:update[] language:text?",
        ["device.update"] = "tag:text new_tag:text",
        ["macro.insert"] = "page:text macro:macro x:mm y:mm variant:zero? numbering:keep?",
        ["page.delete"] = "pages:text[]",
        ["page.renumber"] = "pages:text[] start:integer step:integer? keep_text:boolean? subpages:subpages?",
        ["device.renumber"] = "pages:text[] only_unnumbered:boolean? scheme:text? start:integer? step:integer?",
        ["create"] = "name:text page_type:pageType? description:text?",
        ["update"] = "name:text description:text",
    };

    public static WorkflowDefinition Validate(JsonElement spec, JsonElement configSchema, Func<string, ResourceKind, bool> resolve)
    {
        _ = Read(spec, configSchema, resolve, null, null);
        return new(spec.Clone(), configSchema.Clone());
    }

    public static ImmutableArray<WorkflowStep> Expand(WorkflowDefinition workflow, JsonElement inputs, JsonElement configValues) =>
        Read(workflow.Spec, workflow.ConfigSchema, (_, _) => true, inputs, configValues);

    private static ImmutableArray<WorkflowStep> Read(JsonElement spec, JsonElement configSchema, Func<string, ResourceKind, bool> resolve, JsonElement? inputs, JsonElement? configValues)
    {
        Object(spec, "/spec", "parameters steps", "branches requiredCapabilities applicability acceptance");
        FiniteNumbers(spec, "/spec");
        ConfigValidator.Schema(configSchema);
        JsonElement parameters = spec.GetProperty("parameters");
        Require(parameters.ValueKind == JsonValueKind.Object && parameters.EnumerateObject().Count() <= 50, "/spec/parameters", "must declare at most 50 parameters");
        var items = new JsonObject();
        foreach (JsonProperty p in parameters.EnumerateObject())
        {
            Object(p.Value, Child("/spec/parameters", p.Name), "type required", "unit enum min max default");
            items[p.Name] = JsonNode.Parse(p.Value.GetRawText());
            items[p.Name]!["allowedLayers"] = new JsonArray("base");
        }
        try
        {
            ConfigValidator.Schema(JsonSerializer.SerializeToElement(new { items }));
        }
        catch (PackFormatException ex)
        {
            throw new PackFormatException(ex.Message.Replace("/spec/items", "/spec/parameters", StringComparison.Ordinal), ex);
        }
        if (inputs is { } supplied)
        {
            Object(supplied, "/inputs", "", string.Join(' ', parameters.EnumerateObject().Select(p => p.Name)));
            Require(configValues!.Value.ValueKind == JsonValueKind.Object, "/config", "must contain resolved scalar values");
            foreach (JsonProperty p in parameters.EnumerateObject())
            {
                Require(supplied.TryGetProperty(p.Name, out _) || p.Value.TryGetProperty("default", out _) || !p.Value.GetProperty("required").GetBoolean(), Child("/inputs", p.Name), "is required");
                if (supplied.TryGetProperty(p.Name, out JsonElement value))
                {
                    ConfigValidator.Value(p.Value, value, Child("/inputs", p.Name));
                }
            }
        }
        JsonElement branches = spec.TryGetProperty("branches", out JsonElement declared) ? declared : JsonSerializer.SerializeToElement(new { });
        Require(branches.ValueKind == JsonValueKind.Object && branches.EnumerateObject().Count() <= 20, "/spec/branches", "must declare at most 20 selectors");
        Require(branches.EnumerateObject().All(branch => Reference(branch.Value, Child("/spec/branches", branch.Name)).Type.GetProperty("type").GetString() is "choice" or "boolean"),
            "/spec/branches", "requires finite choice or boolean selectors");
        foreach (JsonProperty list in spec.EnumerateObject().Where(p => p.Name is "requiredCapabilities" or "acceptance"))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Require(list.Value.ValueKind == JsonValueKind.Array && list.Value.GetArrayLength() <= 50 && list.Value.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String
                && value.GetString()!.Length is > 0 and <= 200 && seen.Add(value.GetString()!) && (list.Name != "acceptance" || resolve(value.GetString()!, ResourceKind.Rule) || resolve(value.GetString()!, ResourceKind.Sample))),
                "/spec/" + list.Name, "must contain at most 50 unique capability names or rule/sample references in the resource closure");
        }
        if (spec.TryGetProperty("applicability", out JsonElement predicate))
        {
            JsonElement rule = JsonSerializer.SerializeToElement(new { severity = "mandatory", source = "workflow", scope = new { kind = "function", where = new { literal = true } }, check = predicate, message = new { en = "Workflow applicability" } });
            try
            {
                _ = RuleValidator.Validate(rule, key => TypeOf(configSchema.GetProperty("items"), key), key => TypeOf(parameters, key));
            }
            catch (PackFormatException ex)
            {
                throw new PackFormatException(ex.Message.Replace("/spec/check", "/spec/applicability", StringComparison.Ordinal), ex);
            }
        }
        JsonElement steps = spec.GetProperty("steps");
        Require(steps.ValueKind == JsonValueKind.Array && steps.GetArrayLength() is > 0 and <= 20, "/spec/steps", "must contain 1 to 20 steps, counting all branches");
        var (result, indices, bytes) = (ImmutableArray.CreateBuilder<WorkflowStep>(), new Dictionary<int, int>(), 0L);
        for (int i = 0; i < steps.GetArrayLength(); i++)
        {
            (JsonElement step, string at) = (steps[i], $"/spec/steps/{i}");
            Object(step, at, "op args", "dependsOn branch case");
            string op = step.GetProperty("op").ValueKind == JsonValueKind.String ? step.GetProperty("op").GetString()! : "";
            Require(op.Contains('.', StringComparison.Ordinal) && _shapes.ContainsKey(op), at + "/op", "is not a closed plan operation");
            bool selected = true;
            if (step.TryGetProperty("branch", out JsonElement group))
            {
                Require(group.ValueKind == JsonValueKind.String && branches.TryGetProperty(group.GetString()!, out _), at + "/branch", "must name a declared branch");
                (JsonElement type, JsonElement? value) = Reference(branches.GetProperty(group.GetString()!), at + "/branch");
                Require(step.TryGetProperty("case", out JsonElement choice), at + "/case", "is required with branch");
                ConfigValidator.Value(type, choice, at + "/case");
                Require(inputs is null || value.HasValue, at + "/branch", "selector value is missing");
                selected = inputs is null || value.HasValue && JsonElement.DeepEquals(value.Value, choice);
            }
            Require(!step.TryGetProperty("case", out _) || step.TryGetProperty("branch", out _), at + "/case", "requires a declared branch");
            JsonNode? args = Walk(step.GetProperty("args"), op, at + "/args", selected);
            var dependencies = new List<int>();
            if (step.TryGetProperty("dependsOn", out JsonElement deps))
            {
                Require(deps.ValueKind == JsonValueKind.Array, at + "/dependsOn", "must contain earlier step numbers");
                foreach (JsonElement dependency in deps.EnumerateArray())
                {
                    Require(dependency.ValueKind == JsonValueKind.Number && dependency.TryGetInt32(out _) && dependency.GetInt32() > 0 && dependency.GetInt32() <= i && !dependencies.Contains(dependency.GetInt32()), at + "/dependsOn", "must contain unique earlier step numbers");
                    int n = dependency.GetInt32();
                    JsonElement prior = steps[n - 1];
                    Require(!prior.TryGetProperty("branch", out _) || step.TryGetProperty("branch", out _) && JsonElement.DeepEquals(prior.GetProperty("branch"), step.GetProperty("branch")) && JsonElement.DeepEquals(prior.GetProperty("case"), step.GetProperty("case")), at + "/dependsOn", "cannot depend on another branch");
                    dependencies.Add(n);
                }
            }
            bytes += Encoding.UTF8.GetByteCount(args!.ToJsonString());
            if (selected)
            {
                indices[i + 1] = result.Count + 1;
                result.Add(new(op, JsonSerializer.SerializeToElement(args), [.. dependencies.Select(n => indices[n])]));
            }
        }
        Require(bytes <= 256 * 1024 && result.Count > 0 && steps.EnumerateArray().Sum(s => s.GetProperty("args").TryGetProperty("pages", out JsonElement pages) ? pages.GetArrayLength() : 1) <= 200, "/spec/steps", "exceeds 256 KiB/200 locators or selects no steps");
        return result.ToImmutable();

        (JsonElement Type, JsonElement? Value) Reference(JsonElement node, string at)
        {
            Object(node, at, "", "parameter config");
            Require(node.EnumerateObject().Count() == 1, at, "must contain exactly parameter or config");
            JsonProperty refNode = node.EnumerateObject().Single();
            (JsonElement source, JsonElement? values) = (refNode.Name == "parameter" ? parameters : configSchema.GetProperty("items"), refNode.Name == "parameter" ? inputs : configValues);
            Require(refNode.Value.ValueKind == JsonValueKind.String && source.TryGetProperty(refNode.Value.GetString()!, out _), at, "must name a declared typed value");
            string key = refNode.Value.GetString()!;
            JsonElement type = source.GetProperty(key);
            JsonElement? value = values is { } suppliedValues && suppliedValues.TryGetProperty(key, out JsonElement found) ? found : refNode.Name == "parameter" && type.TryGetProperty("default", out JsonElement fallback) ? fallback : null;
            if (value.HasValue)
            {
                ConfigValidator.Value(type, value.Value, at);
            }
            return (type, value);
        }

        JsonNode? Walk(JsonElement node, string shape, string at, bool selected)
        {
            if (node.ValueKind == JsonValueKind.Object && (node.TryGetProperty("parameter", out _) || node.TryGetProperty("config", out _)))
            {
                (JsonElement type, JsonElement? value) = Reference(node, at);
                string expected = shape is "integer" or "mm" ? "number" : shape;
                Require((type.GetProperty("type").GetString() == expected || expected == "text" && type.GetProperty("type").GetString() == "choice") && (type.TryGetProperty("unit", out JsonElement unit) ? unit.GetString() : null) == (shape == "mm" ? "mm" : null), at, "substitution type/unit must match this scalar argument; paths are literal resources");
                Require(inputs is null || !selected || value.HasValue, at, "substitution value is missing");
                return inputs is null || !selected ? null : Walk(value!.Value, shape, at, selected);
            }
            if (_shapes.TryGetValue(shape, out string? fields))
            {
                string[][] slots = [.. fields.Split(' ').Select(f => f.Split(':'))];
                Object(node, at, string.Join(' ', slots.Where(s => !s[1].EndsWith('?')).Select(s => s[0])), string.Join(' ', slots.Select(s => s[0])));
                return new JsonObject(node.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, Walk(p.Value, slots.Single(s => s[0] == p.Name)[1].TrimEnd('?'), Child(at, p.Name), selected))));
            }
            if (shape.EndsWith("[]", StringComparison.Ordinal))
            {
                Require(node.ValueKind == JsonValueKind.Array && node.GetArrayLength() is > 0 and <= 50, at, "must contain 1 to 50 entries");
                return new JsonArray(node.EnumerateArray().Select((v, i) => Walk(v, shape[..^2], $"{at}/{i}", selected)).ToArray());
            }
            Require(shape switch
            {
                "integer" => node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out int n) && n >= 1,
                "zero" => node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out int n) && n == 0,
                "mm" => node.ValueKind == JsonValueKind.Number && node.GetDecimal() >= 0,
                "boolean" => node.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => node.ValueKind == JsonValueKind.String && (shape switch { "keep" => node.GetString() == "keep", "pageType" => node.GetString() is "Circuit" or "CircuitSingleLine" or "Overview" or "Graphics", "subpages" => node.GetString() is "retain" or "consecutive" or "to_main", "macro" => resolve(node.GetString()!, ResourceKind.Macro), _ => true }),
            }, at, "must match the closed argument schema and resource closure");
            return JsonNode.Parse(node.GetRawText());
        }
    }

    private static RuleValueType? TypeOf(JsonElement items, string key) => items.TryGetProperty(key, out JsonElement item)
        ? new(item.GetProperty("type").GetString() == "choice" ? "text" : item.GetProperty("type").GetString()!, item.TryGetProperty("unit", out JsonElement unit) ? unit.GetString() : null) : null;
}
