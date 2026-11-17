using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

internal static class PlanToolResults
{
    internal static CallToolResult Plan(PlanRecord record, bool dryRun, int budget = ResultShaper.DefaultTokenBudget)
    {
        PlanResult result = dryRun ? PlanResult.Aggregate(record.Definition.Steps.Select(s => new PlanStepResult(s.Index, s.Op, s.Effective, ExecutionState.NotStarted))) : record.State.Result!;
        PlanFailure? failure = result.Error ?? (!dryRun && result.Verification != "passed"
            ? new(ErrorReason.EplanOperationFailed, "Inspect eplan_plan_get: object verification did not pass; committed changes remain committed.", false) : null);
        string execution = JsonNamingPolicy.SnakeCaseLower.ConvertName(result.Execution.ToString());
        JsonObject data = ContextBuilder.Node(new
        {
            record.Definition.PlanId,
            record.Definition.Project,
            execution,
            dryRun,
            completedSteps = result.Steps.Where(s => s.Execution == ExecutionState.Committed).Select(s => s.Index),
            steps = result.Steps.Select(s => new
            {
                s.Index,
                s.Op,
                s.Execution,
                reason = s.Error?.Reason,
                undo = s.UndoDescription,
                s.Recovered,
                changes = dryRun ? record.Definition.Steps.Single(d => d.Index == s.Index).PlannedChanges.ToArray() : null,
            }),
            backup = result.Backups.FirstOrDefault()?.Path,
            stepBackups = result.Steps.Where(s => s.Backup is not null).Select(s => new { step = s.Index, path = s.Backup }),
            verification = result.Verification ?? "skipped",
            validation = new { objectData = result.Validation?.GetProperty("entries"), electrical = "skipped", drawing = "skipped" },
            diff = new { summary = new { completedSteps = result.Steps.Count(s => s.Execution == ExecutionState.Committed) }, entries = result.Diff, coverage = result.Coverage },
            error = failure is null ? null : new TaskError(failure.Reason, failure.Message, result.Retryable, failure.Metadata, [], Guid.NewGuid().ToString("N"), execution),
            truncated = false,
            hint = (string?)null,
            resource = "eplanmcp://plans/" + record.Definition.PlanId,
        });
        data = ContextBuilder.Clean(data).AsObject();
        if (data["error"] is JsonObject error)
        {
            error["metadata"] = Metadata(failure?.Metadata);
        }
        string summary = string.Create(CultureInfo.InvariantCulture, $"Plan {execution}: {result.Steps.Count(s => s.Execution == ExecutionState.Committed)} steps committed; verification {result.Verification ?? "skipped"}.");
        IEnumerable<JsonArray> previews = data["steps"]!.AsArray().Select(s => s?["changes"]).OfType<JsonArray>();
        foreach (JsonArray array in previews.Concat(new[] { data["diff"]?["entries"], data["validation"]?["objectData"], data["diff"]?["coverage"]?["entries"], data["error"]?["metadata"]?["conflicts"] }.OfType<JsonArray>()))
        {
            while (ResultShaper.ResultTokens(data.ToJsonString(HostJson.Options)) + ResultShaper.EstimateTokens(summary) > Math.Min(budget, ResultShaper.MaxTokenBudget) && array.Count > 0)
            {
                int index = array.Count - 1;
                array.RemoveAt(index);
                data["truncated"] = true;
                data["hint"] = "Read eplan_plan_get detailed pages for complete steps, validation and differences.";
            }
        }
        CallToolResult call = ToolResults.Success(summary, data);
        call.IsError = failure is not null;
        return call;
    }

    internal static CallToolResult Failure(Exception exception, bool write)
    {
        TaskError error = exception switch
        {
            TaskRegistryException task => task.Error,
            BridgeCallException bridge => new(bridge.Error.Reason, bridge.Error.Message, bridge.Error.Retryable,
                JsonSerializer.SerializeToElement(bridge.Error.Metadata, HostJson.Options), [], Guid.NewGuid().ToString("N")),
            PlanCapacityException capacity => new(capacity.Reason, capacity.Message, false, JsonSerializer.SerializeToElement(capacity.Metadata), [], Guid.NewGuid().ToString("N")),
            _ => new(ErrorReason.Internal, "Inspect eplan_plan_get and ask the engineer to run 'eplanmcp doctor'; do not replay the plan.", false, null, [], Guid.NewGuid().ToString("N")),
        };
        error = error with { Execution = write ? ExecutionState.NotStarted : null };
        JsonObject data = ContextBuilder.Node(new { error });
        data["error"]!["metadata"] = Metadata(error.Metadata);
        if (write)
        {
            data["execution"] = ExecutionState.NotStarted;
        }
        CallToolResult call = ToolResults.Success("The operation could not proceed; inspect error.", ContextBuilder.Clean(data));
        call.IsError = true;
        return call;
    }

    internal static JsonNode? Metadata(JsonElement? metadata)
    {
        JsonNode? node = metadata is null ? null : JsonNode.Parse(metadata.Value.GetRawText());
        // Only the fixed guard metadata keys have a JSON-in-string contract (design 4.8).
        foreach (string key in new[] { "conflicts", "missing", "violations" })
        {
            try
            {
                if (node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue(out string? json)
                    && JsonNode.Parse(json) is JsonArray array && array.All(item => key == "conflicts"
                        ? item is JsonObject conflict && conflict["reason"] is JsonValue && conflict.ContainsKey("expected") && conflict.ContainsKey("actual")
                        : item is JsonValue text && text.TryGetValue<string>(out _)))
                {
                    obj[key] = array;
                }
            }
            catch (JsonException)
            {
                // Malformed Bridge metadata stays text; it must never discard known execution facts.
            }
        }
        return node is null ? null : ContextBuilder.Clean(node);
    }

    internal static CallToolResult? Validate(JsonElement schema, IDictionary<string, JsonElement>? arguments, bool write)
    {
        List<string> violations = UnknownArgumentFilter.Violations(schema, arguments);
        foreach ((string name, JsonElement value) in arguments ?? new Dictionary<string, JsonElement>())
        {
            if (name is "plan_id" or "task_id" && (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())))
            {
                violations.Add($"'{name}' must be the nonempty ID returned by the original operation.");
            }
            if (name == "wait_seconds" && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int seconds) || seconds is < 0 or > 110))
            {
                violations.Add("'wait_seconds' must be an integer from 0 to 110.");
            }
            if (name == "dry_run" && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                violations.Add("'dry_run' must be a boolean.");
            }
        }
        return violations.Count == 0 ? null : Failure(new TaskRegistryException(new(ErrorReason.InvalidArgument,
            "Correct all listed arguments using the original operation's returned ID.", false, JsonSerializer.SerializeToElement(new { violations }), [], Guid.NewGuid().ToString("N"))), write);
    }
}
