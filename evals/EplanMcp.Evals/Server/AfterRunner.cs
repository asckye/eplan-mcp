using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Server;

/// <summary>
/// What an after call found: the picked value, or in <see cref="Missing"/> why there is none. After calls look at what
/// the model did, so a value they cannot give fails the checks on it, where a failed oracle call makes the task invalid.
/// </summary>
internal sealed record AfterValue(JsonNode? Value, string? Missing);

/// <summary>
/// Runs the after calls of a task once the attempt is over, to see what the model changed (evals/README.md). A tool
/// error is a result too: its <c>{"error": …}</c> object is what the pick reads, so a check can expect
/// <c>$.error.reason</c> to be, say, DEVICE_NOT_FOUND. Only the first page of a list is read. The runner answers
/// <see cref="EvalTask.ExportedFiles"/> itself, from the files the attempt exported.
/// </summary>
internal static class AfterRunner
{
    private const int TextShown = 300;

    /// <summary><paramref name="project"/> is the task's copy, as for <see cref="OracleRunner.WithProject"/>; <paramref name="exports"/> the attempt's <see cref="AgentAttempt.Exports"/>.</summary>
    public static async Task<IReadOnlyDictionary<string, AfterValue>> RunAsync(EvalTask task, IToolClient client, string? project, IReadOnlyList<ExportedFile> exports, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, AfterValue>(StringComparer.Ordinal);
        foreach ((string name, OracleCall call) in task.After)
        {
            if (call.Tool == EvalTask.ExportedFiles)
            {
                JsonObject files = Exported(exports, project, call.Args);
                values[name] = call.Pick.TrySelect(files, out JsonNode? listed)
                    ? new AfterValue(listed?.DeepClone(), null)
                    : new AfterValue(null, $"{call.Pick} matches nothing in {EvalTask.ExportedFiles}, which found {files["count"]} of the attempt's files");
                continue;
            }

            ToolOutcome outcome = await client.CallAsync(call.Tool, OracleRunner.WithProject(call.Args, project), cancellationToken).ConfigureAwait(false);
            string text = outcome.Text.Length > TextShown ? outcome.Text[..TextShown] + "..." : outcome.Text;
            JsonNode? result = outcome.Structured ?? (outcome.IsError ? ErrorOf(outcome.Text) : null);
            values[name] = result is null
                ? new AfterValue(null, $"{call.Tool} returned nothing to pick from: {text}")
                : call.Pick.TrySelect(result, out JsonNode? value)
                    ? new AfterValue(value?.DeepClone(), null)
                    : new AfterValue(null, $"{call.Pick} matches nothing in the result of {call.Tool}: {text}");
        }

        return values;
    }

    /// <summary>
    /// What exported_files finds: <c>{"files": [{"path", "name", "bytes"}], "count"}</c>, paths relative to the export
    /// root. On a copy only the copy's exports count, since the server names each export's folder after the project: a
    /// PDF of the shared fixture is not one of the copy. <c>extension</c> keeps the files with that extension, with or
    /// without its dot, ignoring case.
    /// </summary>
    public static JsonObject Exported(IReadOnlyList<ExportedFile> exports, string? project, JsonObject args)
    {
        string? folder = project is null ? null : Path.GetFileName(project) + Path.DirectorySeparatorChar;
        string? extension = Comparison.Text(args["extension"]) is { } wanted ? "." + wanted.TrimStart('.') : null;
        var files = new JsonArray([.. exports
            .Where(f => folder is null || f.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            .Where(f => extension is null || Path.GetExtension(f.Path).Equals(extension, StringComparison.OrdinalIgnoreCase))
            .Select(f => new JsonObject { ["path"] = f.Path, ["name"] = Path.GetFileName(f.Path), ["bytes"] = f.Bytes })]);
        return new JsonObject { ["files"] = files, ["count"] = files.Count };
    }

    /// <summary>The <c>{"error": …}</c> object eplanmcp writes as the text of a failed call; null for other text.</summary>
    private static JsonObject? ErrorOf(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
