using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Budgeted presentation copies; the stored definition and complete changes text are never shortened.</summary>
internal static class WorkflowResults
{
    public static string Summary(int count) => string.Create(CultureInfo.InvariantCulture, $"Compiled a plan with {count} steps; no project data was changed.");

    public static CallToolResult Success(PlanRecord record, int budget = ResultShaper.DefaultTokenBudget)
    {
        PlanDefinition d = record.Definition;
        JsonObject data = ContextBuilder.Clean(JsonSerializer.SerializeToNode(d, PlanJson.Options)!).AsObject();
        foreach (string field in new[] { "owner", "compilerVersion", "snapshotFormatVersion", "eplanVersion", "eplanVariant", "requiredOperations", "resources", "digests", "sources" })
        {
            data.Remove(field);
        }
        data["project"]!.AsObject().Remove("guid");
        foreach ((JsonNode? step, int i) in data["steps"]!.AsArray().Select((s, i) => (s, i)))
        {
            step!["tier"] = d.Steps[i].Tier.ToString();
            step["prediction"] ??= false;
        }
        data["planHash"] = record.PlanHash;
        data["tier"] = d.Steps.Any(s => s.Tier == Safety.ToolTier.Destructive) ? "Destructive" : "Write";
        data["status"] = "compiled";
        data["resource"] = "eplanmcp://plans/" + d.PlanId;
        data["diffPreview"] = JsonSerializer.SerializeToNode(new
        {
            summary = new
            {
                steps = d.Steps.Length,
                changes = d.Steps.Sum(s => s.PlannedChanges.Length),
            },
            entries = d.Steps.Select(s => new
            {
                step = s.Index,
                pages = s.ModifyScope.Pages,
                changes = s.PlannedChanges,
            }),
        }, HostJson.Options);
        data["diffPreview"] = ContextBuilder.Clean(data["diffPreview"]!);
        data["counts"] = data["diffPreview"]!["summary"]!.DeepClone();
        data["truncated"] = false;
        data["hint"] = null;
        string summary = Summary(d.Steps.Length);
        var link = new ResourceLinkBlock
        {
            Uri = "eplanmcp://plans/" + d.PlanId,
            Name = "Workflow plan",
            MimeType = "application/json",
        };
        int Tokens() => ResultShaper.ResultTokens(data.ToJsonString(HostJson.Options)) + ResultShaper.EstimateTokens(summary)
            + ResultShaper.EstimateTokens(JsonSerializer.Serialize(link, HostJson.Options));
        if (Tokens() > Math.Min(budget, ResultShaper.MaxTokenBudget) && data.Remove("engineering"))
        { data["truncated"] = true; data["hint"] = "Read the complete stored engineering evidence with eplan_plan_get."; }
        foreach (JsonArray entries in new[] { data["diffPreview"]!["entries"]!.AsArray(), data["steps"]!.AsArray(), data["inputs"]!["steps"]!.AsArray() })
        {
            while (Tokens() > Math.Min(budget, ResultShaper.MaxTokenBudget) && entries.Count > 0)
            {
                entries.RemoveAt(entries.Count - 1);
                data["truncated"] = true;
                data["hint"] = "Returned entries were shortened. Keep the plan reference to retrieve the complete stored plan.";
            }
        }
        CallToolResult result = ToolResults.Success(summary, data);
        result.Content.Add(link);
        return result;
    }

    public static CallToolResult Error(string reason, string message, object? metadata = null, bool retryable = false)
    {
        JsonElement data = JsonSerializer.SerializeToElement(new
        {
            error = new TaskError(reason, message, retryable,
            metadata is null ? null : JsonSerializer.SerializeToElement(metadata, HostJson.Options), [], Guid.NewGuid().ToString("N"))
        }, HostJson.Options);
        return new CallToolResult
        {
            IsError = true,
            StructuredContent = data,
            Content = [new TextContentBlock
            {
                Text = data.GetRawText(),
            }],
        };
    }

    public static CallToolResult BridgeError(BridgeError error) => Error(error.Reason, error.Message, error.Metadata, error.Retryable);

    public static void Configure(IEnumerable<McpServerTool> tools)
    {
        McpServerTool tool = tools.Single(t => t.ProtocolTool.Name == WorkflowTools.PlanTool);
        JsonNode input = JsonNode.Parse(PlanStepCatalog.InputSchema.GetRawText())!;
        foreach (JsonNode? step in input["properties"]!["steps"]!["items"]!["oneOf"]!.AsArray())
        {
            if (step!["properties"]!["args"]!["properties"]!["language"] is JsonObject language)
            {
                language["description"] = "Omitted: EPLAN interface language, named in changes; a different language at apply is PLAN_STALE.";
            }
            if (step["properties"]!["args"]!["properties"]!["scheme"] is JsonObject scheme)
            {
                scheme["description"] = "Explicit scheme required; Bridge cannot expose its current default.";
            }
        }
        input["properties"]!["cabinet"] = JsonNode.Parse("""{"type":"string"}""");
        input["properties"]!["workflow"] = JsonNode.Parse("""{"type":"string"}""");
        input["properties"]!["inputs"] = JsonNode.Parse("""{"type":"object","additionalProperties":{"type":["string","number","boolean"]}}""");
        input["required"] = new JsonArray("project");
        input["oneOf"] = JsonNode.Parse("""[{"required":["steps"],"not":{"anyOf":[{"required":["workflow"]},{"required":["inputs"]}]}},{"required":["workflow"],"not":{"required":["steps"]}}]""");
        tool.ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(input);
        var properties = new JsonObject();
        foreach (string name in new[] { "schemaVersion", "planId", "planHash", "project", "cabinet", "bindingRevision", "lockDigest", "contextRef", "inputs", "tier", "status", "createdAt", "expiresAt", "steps", "diffPreview", "counts", "truncated", "hint", "resource" })
        {
            string type = name is "schemaVersion" ? "integer" : name is "bindingRevision" ? "integer" : name is "steps" ? "array"
                : name is "project" or "inputs" or "diffPreview" or "counts" ? "object" : name == "truncated" ? "boolean" : "string";
            properties[name] = new JsonObject
            {
                ["type"] = name is "bindingRevision" or "lockDigest" or "contextRef" or "hint" ? new JsonArray(type, "null") : JsonValue.Create(type),
            };
        }
        properties["planHash"]!["pattern"] = "^[a-f0-9]{64}$";
        properties["planId"]!["pattern"] = "^[a-f0-9]{32}$";
        properties["tier"]!["enum"] = new JsonArray("Write", "Destructive");
        properties["status"]!["const"] = "compiled";
        properties["steps"]!["items"] = new JsonObject
        {
            ["type"] = "object",
        };
        foreach (string field in new[] { "createdAt", "expiresAt" })
        {
            properties[field]!["format"] = "date-time";
        }
        var success = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = new JsonArray([.. properties.Select(p => JsonValue.Create(p.Key))]),
        };
        var failure = JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["error"],"properties":{"error":{"type":"object","required":["reason","message","retryable","metadata","eplanMessages","correlationId"]}}}""");
        properties["engineering"] = JsonNode.Parse("""{"type":"object"}""");
        tool.ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["anyOf"] = new JsonArray(success, failure),
        });
    }
}
