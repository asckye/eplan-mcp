using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Post-processing of the schemas the SDK generates. The SDK omits <c>additionalProperties: false</c> (spike S4,
/// api-notes C), which engineering-handbook.html#pa requires so a misspelled argument is rejected instead of ignored.
/// </summary>
internal static class ToolSchemas
{
    public static void Harden(IEnumerable<McpServerTool> tools)
    {
        foreach (McpServerTool tool in tools)
        {
            if (JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) is JsonObject schema
                && schema["type"]?.GetValue<string>() == "object"
                && !schema.ContainsKey("additionalProperties"))
            {
                schema["additionalProperties"] = false;
                schema["properties"] ??= new JsonObject();
                if (tool.ProtocolTool.Name == ConnectionCreateTools.Tool)
                {
                    schema["properties"]!["layout_guard"]?.AsObject().Remove("type");
                    schema["properties"]!["items"] = ConnectionCreateSchema.Items();
                    schema["$defs"] = schema["properties"]!["items"]!["$defs"]!.DeepClone();
                    schema["properties"]!["items"]!.AsObject().Remove("$defs");
                    tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["project","page","execution","dryRun","undoSteps","recovered"],"properties":{"project":{"type":"string"},"page":{"type":"string"},"execution":{"enum":["not_started","committed"]},"dryRun":{"type":"boolean"},"undoSteps":{"enum":[1,2,6,7,8]},"recovered":{"type":"boolean"},"warning":{"type":["string","null"]},"pageShown":{"type":["boolean","null"]},"hint":{"type":["string","null"]},"truncated":{"type":"boolean"},"coordinates":{"type":"array"},"symbols":{"type":"array"},"graphicalAutoconnect":{"type":"array"},"logicalAfterGenerate":{"type":"array"},"actualEndpoints":{"type":"array"},"cableDefinitions":{"type":"array"},"coreImpact":{"type":"array"},"actualCores":{"type":"array"},"definitionPoints":{"type":"array"},"definitionPointCount":{"type":"integer","minimum":1},"gridSize":{"type":"number"},"pageSize":{"type":"array"},"coverage":{"type":"string"},"logicalDuringPlacement":{"const":0},"generation":{"type":"string"},"fingerprint":{"type":"string"}}}"""));
                }
                if (tool.ProtocolTool.Name == PageReviewTools.Tool)
                { PageReviewTools.Configure(tool.ProtocolTool, schema); }
                if (tool.ProtocolTool.Name == DeviceWriteTools.PlaceTool)
                { DevicePlaceInputs.Configure(schema); }
                if (tool.ProtocolTool.Name == MacroWriteTools.InsertTool)
                {
                    ConfigureMacroInsert(schema);
                }
                if (tool.ProtocolTool.Name is ConnectionCreateTools.Tool or DeviceWriteTools.PlaceTool)
                {
                    var output = JsonNode.Parse(tool.ProtocolTool.OutputSchema!.Value.GetRawText())!.AsObject();
                    output["properties"]!["metadata"] = JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{"plugins":{"type":"array","items":{"type":"string"}},"warnings":{"type":"array","items":{"type":"string"}}}}""");
                    tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(output);
                }
                if (tool.ProtocolTool.Name == ListImportTools.ImportTool)
                {
                    schema["allOf"] = JsonNode.Parse("""[{"if":{"properties":{"kind":{"const":"parts"}}},"then":{"properties":{"format":{"const":"xml"}}}}]""");
                    var output = JsonNode.Parse(tool.ProtocolTool.OutputSchema!.Value.GetRawText())!.AsObject();
                    RequireProperties(output);
                    tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(output);
                }

                // Objects inside array arguments too (eplan_page_create's pages), which UnknownArgumentFilter checks.
                foreach (JsonObject items in schema["properties"]!.AsObject().Select(p => (p.Value as JsonObject)?["items"]).OfType<JsonObject>())
                {
                    if (items["type"] is JsonValue type && type.TryGetValue(out string? kind) && kind == "object" && !items.ContainsKey("additionalProperties"))
                    {
                        items["additionalProperties"] = false;
                    }

                    if (tool.ProtocolTool.Name is PageWriteTools.CreateTool or PageWriteTools.UpdateTool)
                    {
                        items["properties"]!["description"] = JsonNode.Parse("""{"oneOf":[{"type":"string","maxLength":500},{"type":"object","minProperties":1,"propertyNames":{"pattern":"^[a-z]{2}_[A-Z]{2}$"},"additionalProperties":{"type":"string","maxLength":500}},{"type":"null"}]}""");
                    }
                    if (tool.ProtocolTool.Name == PageWriteTools.UpdateTool)
                    {
                        items["anyOf"] = JsonNode.Parse("""[{"required":["description"]},{"required":["plot_frame"]},{"required":["grid_size_mm"]}]""");
                    }
                }

                tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(schema);
            }
        }
    }

    private static void ConfigureMacroInsert(JsonObject schema)
    {
        string[] itemFields = ["page", "macro", "part", "part_variant", "representation", "x", "y", "variant", "numbering", "macro_box", "values"];
        JsonObject properties = schema["properties"]!.AsObject();
        foreach (string field in itemFields)
        {
            JsonObject property = properties[field]!.AsObject();
            if (field is not ("variant" or "numbering" or "representation" or "macro_box"))
            {
                property.Remove("default");
            }
            if (property["type"] is JsonArray nullableTypes)
            {
                property["type"] = nullableTypes.First(type => type!.GetValue<string>() != "null")!.DeepClone();
            }
        }
        properties["variant"]!["default"] = 0;
        properties["numbering"]!["default"] = "number";
        properties["representation"]!["default"] = "MultiLine";
        properties["macro_box"]!["default"] = true;
        properties["values"]!["additionalProperties"] = new JsonObject { ["type"] = "string" };
        properties["values"]!["minProperties"] = 1;
        properties["values"]!["maxProperties"] = 20;
        properties["items"] = JsonNode.Parse("""
            {"type":"array","minItems":1,"maxItems":20,"items":{
              "type":"object","properties":{},"required":["page","x","y"],"additionalProperties":false,
              "oneOf":[{"required":["macro"]},{"required":["part"]}],"dependentRequired":{"part_variant":["part"]}}}
            """);
        foreach (string field in itemFields)
        {
            properties["items"]!["items"]!["properties"]![field] = properties[field]!.DeepClone();
        }
        schema["required"] = new JsonArray("project");
        schema["dependentRequired"] = properties["items"]!["items"]!["dependentRequired"]!.DeepClone();
        schema["oneOf"] = JsonNode.Parse("""
            [{"required":["page","x","y"],"oneOf":[{"required":["macro"]},{"required":["part"]}],"not":{"required":["items"]}},
             {"required":["items"],"propertyNames":{"not":{"enum":[]}}}]
            """);
        schema["oneOf"]![1]!["propertyNames"]!["not"]!["enum"] = new JsonArray([.. itemFields.Select(field => (JsonNode?)JsonValue.Create(field))]);
    }

    private static void RequireProperties(JsonObject schema)
    {
        if (schema["properties"] is JsonObject properties)
        {
            schema["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)JsonValue.Create(p.Key))]);
            schema["additionalProperties"] = false;
            foreach ((string name, JsonNode? child) in properties)
            {
                if (child is JsonObject field)
                {
                    if (name is "deviceListEntries" or "projectParts" or "articleReferences")
                    {
                        field["minimum"] = 0;
                    }

                    if (name == "level")
                    {
                        field["enum"] = new JsonArray("message", "warning", "error", "fatal");
                    }

                    RequireProperties(field);
                }
            }
        }

        if (schema["items"] is JsonObject items)
        {
            RequireProperties(items);
        }
    }
}
