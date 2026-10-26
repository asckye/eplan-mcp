using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Plans;

/// <summary>Closed catalog derived from the existing tools, without registering or invoking them.</summary>
internal static class PlanStepCatalog
{
    private static readonly (string Op, Type Tool, string Method, ToolTier Tier)[] _entries =
    [
        ("page.create", typeof(PageWriteTools), nameof(PageWriteTools.PageCreateAsync), ToolTier.Write),
        ("page.update", typeof(PageWriteTools), nameof(PageWriteTools.PageUpdateAsync), ToolTier.Write),
        ("device.update", typeof(DeviceWriteTools), nameof(DeviceWriteTools.DeviceUpdateAsync), ToolTier.Write),
        ("macro.insert", typeof(MacroWriteTools), nameof(MacroWriteTools.MacroInsertAsync), ToolTier.Write),
        ("page.delete", typeof(PageDestructiveTools), nameof(PageDestructiveTools.PageDeleteAsync), ToolTier.Destructive),
        ("page.renumber", typeof(PageDestructiveTools), nameof(PageDestructiveTools.PageRenumberAsync), ToolTier.Destructive),
        ("device.renumber", typeof(DeviceDestructiveTools), nameof(DeviceDestructiveTools.DeviceRenumberAsync), ToolTier.Destructive),
    ];
    private static readonly Dictionary<string, JsonObject> _schemas = _entries.ToDictionary(e => e.Op, e =>
    {
        using ServiceProvider services = new ServiceCollection().AddSingleton<WritePipeline>().BuildServiceProvider();
        McpServerTool tool = McpServerTool.Create(e.Tool.GetMethod(e.Method)!, options: new() { Services = services });
        ToolSchemas.Harden([tool]);
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!.AsObject();
        schema["properties"]!.AsObject().Remove("project");
        schema["properties"]!.AsObject().Remove("dry_run");
        schema["required"]!.AsArray().Remove(schema["required"]!.AsArray().Single(n => n!.GetValue<string>() == "project"));
        if (e.Op == "page.update") // Workflow predictions currently cover descriptions only.
        {
            JsonObject item = schema["properties"]!["pages"]!["items"]!.AsObject();
            item["properties"]!.AsObject().Remove("plot_frame");
            item["properties"]!["description"]!["type"] = "string";
            item["required"] = new JsonArray("name", "description");
            item.Remove("anyOf");
        }

        return schema;
    }, StringComparer.Ordinal);

    public static JsonElement InputSchema => BuildInputSchema();
    public static bool Offers(string op) => _schemas.ContainsKey(op);
    public static ToolTier Tier(string op) => _entries.Single(e => e.Op == op).Tier;
    public static JsonObject ArgsSchema(string op) => (JsonObject)_schemas[op].DeepClone();

    public static List<string> Violations(string op, JsonObject args)
    {
        string[]? pages = args["pages"] is JsonArray array ? new string[array.Count] : null;
        return op switch
        {
            "page.create" or "page.update" => PageWriteTools.Violations("plan", pages?.Length, args["language"]?.ToString()),
            "page.delete" => PageDestructiveTools.DeleteViolations("plan", pages),
            "page.renumber" => PageDestructiveTools.RenumberViolations("plan", pages, Number(args["start"], 0), Number(args["step"], 1)),
            "device.renumber" => DeviceDestructiveTools.Violations("plan", pages, Number(args["start"], 1), Number(args["step"], 1)),
            _ => ToolArguments.WriteProjectViolations("plan"),
        };
    }

    private static int Number(JsonNode? node, int fallback) => node is JsonValue v && v.TryGetValue(out int n) ? n : fallback;

    private static JsonElement BuildInputSchema()
    {
        var alternatives = new JsonArray();
        foreach (var entry in _entries)
        {
            JsonObject args = ArgsSchema(entry.Op);
            AddPageReferences(args, "", entry.Op);
            if (args["properties"]!["pages"] is JsonObject pages)
            {
                pages["minItems"] = 1;
                pages["maxItems"] = 50;
            }
            if (entry.Op == "device.renumber")
            {
                args["required"]!.AsArray().Add("pages");
                args["properties"]!["pages"]!["type"] = "array";
                args["properties"]!["pages"]!.AsObject().Remove("default");
            }
            if (entry.Op == "macro.insert")
            {
                args["properties"]!["variant"]!["const"] = 0;
                args["properties"]!["numbering"]!["const"] = "keep";
                args["properties"]!["numbering"]!["default"] = "keep";
            }
            var step = JsonNode.Parse("""{"type":"object","properties":{"op":{},"args":{},"depends_on":{"type":"array","items":{"type":"integer","minimum":1},"uniqueItems":true,"default":[]}},"required":["op","args"],"additionalProperties":false}""")!;
            step["properties"]!["op"]!["const"] = entry.Op;
            step["properties"]!["args"] = args;
            alternatives.Add(step);
        }
        var schema = JsonNode.Parse("""{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"project":{"type":"string"},"steps":{"type":"array","minItems":1,"maxItems":20}},"required":["project","steps"],"additionalProperties":false}""")!;
        schema["properties"]!["steps"]!["items"] = new JsonObject { ["oneOf"] = alternatives };
        return JsonSerializer.SerializeToElement(schema);
    }

    private static void AddPageReferences(JsonObject schema, string field, string op)
    {
        if (IsPageSlot(op, field))
        {
            JsonNode original = schema.DeepClone();
            schema.Clear();
            schema["oneOf"] = new JsonArray(original, JsonNode.Parse("""{"type":"object","properties":{"from_step":{"type":"integer","minimum":1},"page":{"type":"string"}},"required":["from_step","page"],"additionalProperties":false}"""));
        }
        else if (schema["properties"] is JsonObject properties)
        {
            foreach (var p in properties)
            {
                AddPageReferences(p.Value!.AsObject(), p.Key, op);
            }
        }
        else if (schema["items"] is JsonObject items)
        {
            AddPageReferences(items, field == "pages" ? "page_item" : "", op);
        }
    }

    internal static bool IsPageSlot(string op, string field) => field == "page" && op == "macro.insert" || field == "name" && op == "page.update";
}
