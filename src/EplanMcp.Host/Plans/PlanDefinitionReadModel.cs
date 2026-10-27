using System.Text.Json;

namespace EplanMcp.Host.Plans;

/// <summary>Version dispatch and engineering shape checks; later compilers resolve the evidence semantics.</summary>
internal static class PlanDefinitionReadModel
{
    internal static void Record(JsonElement root)
    {
        int schema = Schema(root, "");
        Require(root.TryGetProperty("definition", out JsonElement definition), "/definition", "is required");
        Definition(definition, "/definition");
        Require(schema == Schema(definition, "/definition"), "/schemaVersion", "must match the definition schema");
    }

    internal static void Definition(JsonElement root, string path = "")
    {
        int schema = Schema(root, path);
        Require(root.TryGetProperty("compilerVersion", out JsonElement compiler) && compiler.ValueKind == JsonValueKind.String,
            path + "/compilerVersion", "must be text");
        if (!(schema == 1 ? compiler.GetString() is "1" or "2" : compiler.GetString() == "3"))
        {
            throw new NotSupportedException(path + "/compilerVersion: Unknown compiler for this plan schema; keep the record and refuse execution.");
        }
        bool present = root.TryGetProperty("engineering", out JsonElement engineering);
        Require(schema == 1 ? !present : present, path + "/engineering", schema == 1 ? "is forbidden in schema 1" : "is required in schema 2");
        if (schema == 1)
        {
            return;
        }

        string at = path + "/engineering";
        Object(engineering, at, "cabinet configValuesAndTrace configDigest workflowRefAndInputs moduleBindings ruleDigests acceptance");
        Text(engineering.GetProperty("cabinet"), at + "/cabinet");
        JsonElement digest = engineering.GetProperty("configDigest");
        Require(digest.ValueKind == JsonValueKind.String && PlanPaths.IsHex(digest.GetString()!, 64), at + "/configDigest", "must be a lowercase SHA-256");
        Each(engineering.GetProperty("configValuesAndTrace"), at + "/configValuesAndTrace", (item, itemPath) =>
        {
            Object(item, itemPath, "key value unit constraints trace");
            Text(item.GetProperty("key"), itemPath + "/key");
            Require(item.GetProperty("value").ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False,
                itemPath + "/value", "must be text, number or boolean");
            if (item.GetProperty("unit").ValueKind != JsonValueKind.Null)
            {
                Text(item.GetProperty("unit"), itemPath + "/unit");
            }
            Object(item.GetProperty("constraints"), itemPath + "/constraints");
            Each(item.GetProperty("trace"), itemPath + "/trace", (trace, pointer) =>
            {
                Object(trace, pointer, "layer source pointer kind value");
                Text(trace.GetProperty("layer"), pointer + "/layer");
                Object(trace.GetProperty("source"), pointer + "/source");
                Require(trace.GetProperty("pointer").ValueKind == JsonValueKind.String, pointer + "/pointer", "must be text");
                Text(trace.GetProperty("kind"), pointer + "/kind");
            });
        });
        Object(engineering.GetProperty("workflowRefAndInputs"), at + "/workflowRefAndInputs");
        Each(engineering.GetProperty("moduleBindings"), at + "/moduleBindings", (item, itemPath) => Object(item, itemPath));
        Object(engineering.GetProperty("ruleDigests"), at + "/ruleDigests");
        Object(engineering.GetProperty("acceptance"), at + "/acceptance");
        FiniteNumbers(engineering, at);
    }

    private static int Schema(JsonElement root, string at)
    {
        Require(root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schemaVersion", out _), at + "/schemaVersion", "is required");
        JsonElement value = root.GetProperty("schemaVersion");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int schema) || schema is not (1 or 2))
        {
            throw new NotSupportedException(at + "/schemaVersion: Unknown plan schema; keep the record and refuse execution.");
        }
        return schema;
    }

    private static void Object(JsonElement node, string at, string? members = null)
    {
        Require(node.ValueKind == JsonValueKind.Object, at, "must be an object");
        if (members is null)
        {
            return;
        }
        string[] names = members.Split(' ');
        foreach (string name in names)
        {
            Require(node.TryGetProperty(name, out _), Child(at, name), "is required");
        }
        foreach (JsonProperty member in node.EnumerateObject())
        {
            Require(names.Contains(member.Name, StringComparer.Ordinal), Child(at, member.Name), "is not a member here");
        }
    }

    private static void Each(JsonElement node, string at, Action<JsonElement, string> check)
    {
        Require(node.ValueKind == JsonValueKind.Array, at, "must be an array");
        foreach ((JsonElement item, int index) in node.EnumerateArray().Select((item, index) => (item, index)))
        {
            check(item, $"{at}/{index}");
        }
    }

    private static void FiniteNumbers(JsonElement node, string at)
    {
        Require(node.ValueKind != JsonValueKind.Number || node.TryGetDecimal(out _), at, "must be a finite decimal");
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty member in node.EnumerateObject())
            {
                FiniteNumbers(member.Value, Child(at, member.Name));
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            Each(node, at, FiniteNumbers);
        }
    }

    private static void Text(JsonElement node, string at) => Require(node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(node.GetString()), at, "must be nonempty text");
    private static string Child(string at, string name) => at + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static void Require(bool valid, string at, string message)
    {
        if (!valid)
        {
            throw new JsonException($"{at}: {message}");
        }
    }
}
