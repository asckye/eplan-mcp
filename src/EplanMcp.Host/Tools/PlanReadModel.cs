using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>One immutable store snapshot supplies the tool and Resource; no Bridge dependency.</summary>
internal static class PlanReadModel
{
    private sealed record Cursor(string PlanId, string PlanHash, long Revision, string ResponseFormat, int Offset);

    internal static CallToolResult Page(PlanRecord record, string format, int limit, string? cursor, int? tokenBudget = null)
    {
        if (format is not ("concise" or "detailed") || limit is < 1 or > 200 || (format == "concise" && cursor is not null))
        {
            return Invalid("Use concise or detailed, limit 1 to 200, and cursors only with detailed.");
        }
        bool detailed = format == "detailed";
        List<JsonObject> all = PlanEntries.Build(record, detailed);
        int offset = 0;
        if (cursor is not null)
        {
            try
            {
                byte[] bytes = Base64Url.DecodeFromChars(cursor.Length <= 2048 ? cursor : "!");
                Cursor decoded = PlanJson.Read<Cursor>(Encoding.UTF8.GetString(bytes));
                if (Base64Url.EncodeToString(bytes) != cursor || decoded != new Cursor(record.Definition.PlanId, record.PlanHash, record.State.Revision, format, decoded.Offset)
                    || decoded.Offset <= 0 || decoded.Offset >= all.Count)
                {
                    return Invalid("The cursor no longer matches this plan, hash, revision or format. Read from the first page without cursor.");
                }
                offset = decoded.Offset;
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return Invalid("The cursor is invalid. Read from the first page without cursor.");
            }
        }
        JsonObject plan = PlanEntries.Clean(JsonSerializer.SerializeToNode(record.Definition, PlanJson.Options))!.AsObject();
        foreach (string key in new[] { "owner", "steps", "inputs" })
        {
            plan.Remove(key);
        }
        plan["planHash"] = record.PlanHash;
        plan["tier"] = record.Definition.Steps.Any(s => s.Tier == Safety.ToolTier.Destructive) ? "Destructive" : "Write";
        JsonObject? result = PlanEntries.Clean(JsonSerializer.SerializeToNode(record.State.Result, PlanJson.Options))?.AsObject();
        foreach (string key in new[] { "steps", "validation", "diff" })
        {
            result?.Remove(key);
        }
        if (result is not null)
        {
            result["validation"] = PlanEntries.Summary(JsonSerializer.SerializeToNode(record.State.Result?.Validation));
            result["diff"] = PlanEntries.Summary(JsonSerializer.SerializeToNode(record.State.Result?.Diff));
        }
        int committed = record.State.Result?.Steps.Count(s => s.Execution == ExecutionState.Committed) ?? 0;
        var data = ContextBuilder.Node(new
        {
            record.Definition.PlanId,
            record.Definition.Project,
            record.Definition.Cabinet,
            record.State.Status,
            record.State.Revision,
            record.State.TaskId,
            record.Definition.CreatedAt,
            record.Definition.ExpiresAt,
            Summary = new
            {
                TotalSteps = record.Definition.Steps.Length,
                CompletedSteps = committed,
                FailedSteps = record.State.Result?.Steps.Count(s => s.Error is not null) ?? 0,
                OutsideScope = PlanEntries.Items(JsonSerializer.SerializeToNode(record.State.Result?.Diff)).Count(v => v?["class"]?.GetValue<string>() == "outside_scope"),
            },
            Total = all.Count,
            Resource = "eplanmcp://plans/" + record.Definition.PlanId,
        });
        data["project"] = PlanEntries.Clean(data["project"]);
        data["status"] = JsonSerializer.SerializeToNode(record.State.Status, PlanJson.Options);
        data["plan"] = plan;
        data["result"] = result;
        var entries = new JsonArray();
        data["entries"] = entries;
        string summary = string.Create(CultureInfo.InvariantCulture, $"Plan has {committed} of {record.Definition.Steps.Length} steps committed; inspect result for verification.");
        var link = new ResourceLinkBlock
        {
            Uri = data["resource"]!.GetValue<string>(),
            Name = "Workflow plan",
            MimeType = "application/json",
        };
        int budget = Math.Min(tokenBudget ?? (detailed ? ResultShaper.MaxTokenBudget : ResultShaper.DefaultTokenBudget), ResultShaper.MaxTokenBudget);
        int Tokens() => ResultShaper.ResultTokens(data.ToJsonString(HostJson.Options)) + ResultShaper.EstimateTokens(summary)
            + ResultShaper.EstimateTokens(JsonSerializer.Serialize(link, HostJson.Options));
        void Paging()
        {
            int next = offset + entries.Count;
            data["truncated"] = next < all.Count;
            data["nextCursor"] = detailed && next < all.Count
                ? Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new Cursor(record.Definition.PlanId, record.PlanHash, record.State.Revision, format, next), HostJson.Options)) : null;
            data["hint"] = next < all.Count ? "Read detailed from the first page or pass nextCursor for the remaining whole entries." : null;
        }
        Paging();
        for (int i = offset; i < Math.Min(all.Count, offset + limit); i++)
        {
            entries.Add(all[i]);
            Paging();
            if (Tokens() > budget)
            {
                int minimum = Tokens();
                entries.RemoveAt(entries.Count - 1);
                Paging();
                if (entries.Count == 0)
                {
                    return WorkflowResults.Error(ErrorReason.InvalidArgument, "The necessary envelope and next entry exceed the budget. Ask the engineer to inspect the retained record.", new
                    {
                        minimumEstimatedTokens = minimum,
                    });
                }
                break;
            }
        }
        if (Tokens() > budget)
        {
            return WorkflowResults.Error(ErrorReason.InvalidArgument, "The necessary envelope exceeds the budget. Ask the engineer to inspect the retained record.", new
            {
                minimumEstimatedTokens = Tokens(),
            });
        }
        CallToolResult call = ToolResults.Success(summary, data);
        call.Content.Add(link);
        return call;
    }

    private static CallToolResult Invalid(string message) => WorkflowResults.Error(ErrorReason.InvalidArgument, message, new
    {
        violations = new[] { message },
    });
}
