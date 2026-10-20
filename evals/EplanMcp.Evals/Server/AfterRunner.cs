using System.Text.Json;
using System.Text.Json.Nodes;
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
/// <c>$.error.reason</c> to be, say, DEVICE_NOT_FOUND. Only the first page of a list is read.
/// </summary>
internal static class AfterRunner
{
    private const int TextShown = 300;

    /// <summary><paramref name="project"/> is the task's copy, as for <see cref="OracleRunner.WithProject"/>.</summary>
    public static async Task<IReadOnlyDictionary<string, AfterValue>> RunAsync(EvalTask task, IToolClient client, string? project, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, AfterValue>(StringComparer.Ordinal);
        foreach ((string name, OracleCall call) in task.After)
        {
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
