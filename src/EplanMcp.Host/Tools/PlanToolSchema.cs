using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

internal static class PlanToolSchema
{
    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        JsonObject error = Shape(("reason", "string"), ("message", "string"), ("retryable", "boolean"),
            ("metadata", "object?"), ("eplanMessages", "array"), ("correlationId", "string"));
        error["additionalProperties"] = true; // Write errors additionally retain execution.
        JsonObject failure = Shape(("error", "object"));
        failure["properties"]!["error"] = error.DeepClone();
        JsonObject terminal = Shape(("planId", "string"), ("project", "object"), ("execution", "string"), ("dryRun", "boolean"),
            ("completedSteps", "array"), ("steps", "array"), ("backup", "string?"), ("stepBackups", "array"),
            ("verification", "string"), ("validation", "object"), ("diff", "object"), ("error", "object?"),
            ("truncated", "boolean"), ("hint", "string?"), ("resource", "string"));
        terminal["properties"]!["execution"]!["enum"] = new JsonArray("not_started", "rolled_back", "committed", "partially_committed", "unknown");
        terminal["properties"]!["verification"]!["enum"] = new JsonArray("passed", "failed", "skipped");
        JsonObject receipt = Shape(("taskId", "string"), ("status", "string"), ("planId", "string"));
        JsonObject task = Shape(("taskId", "string"), ("kind", "string"), ("planId", "string"), ("status", "string"),
            ("progress", "object?"), ("result", "object?"), ("error", "object?"), ("createdAt", "string"),
            ("updatedAt", "string"), ("startedAt", "string?"), ("completedAt", "string?"), ("expiresAt", "string?"));
        task["properties"]!["kind"]!["const"] = "plan_apply";
        task["properties"]!["progress"] = Shape(("message", "string"), ("completedSteps", "integer?"), ("totalSteps", "integer?"));
        task["properties"]!["result"] = Shape(("planId", "string"), ("revision", "integer"), ("resource", "string"));
        task["properties"]!["error"] = error.DeepClone();
        foreach (string field in new[] { "progress", "result", "error" })
        {
            task["properties"]![field]!["type"] = new JsonArray("object", "null");
        }
        task["properties"]!["status"]!["enum"] = new JsonArray("queued", "running", "waiting_approval", "succeeded", "failed", "cancelled", "interrupted", "unknown");
        foreach (string time in new[] { "createdAt", "updatedAt", "startedAt", "completedAt", "expiresAt" })
        {
            task["properties"]![time]!["format"] = "date-time";
        }
        foreach ((string name, JsonArray branches) in new[]
        {
            (PlanApplyTools.ApplyTool, new JsonArray(terminal, receipt, Shape(("execution", "string"), ("error", "object")))) ,
            (PlanApplyTools.TaskTool, new JsonArray(task, failure)),
        })
        {
            if (name == PlanApplyTools.ApplyTool)
            {
                branches[2]!["properties"]!["execution"]!["const"] = "not_started";
                branches[2]!["properties"]!["error"] = error.DeepClone();
            }
            tools.Single(t => t.ProtocolTool.Name == name).ProtocolTool.OutputSchema = JsonSerializer.SerializeToElement(new JsonObject
            {
                ["type"] = "object",
                ["anyOf"] = branches,
            });
        }
    }

    private static JsonObject Shape(params (string Name, string Type)[] fields)
    {
        var properties = new JsonObject();
        foreach ((string name, string type) in fields)
        {
            properties[name] = new JsonObject { ["type"] = type.EndsWith('?') ? new JsonArray(type[..^1], "null") : JsonValue.Create(type) };
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = new JsonArray([.. fields.Select(f => JsonValue.Create(f.Name))]),
        };
    }
}
