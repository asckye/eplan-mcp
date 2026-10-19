using System.Text.Json.Nodes;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Server;

/// <summary>
/// The expected values of a task. <see cref="Problems"/> make the task invalid for this run (design 4.2, step 2);
/// <see cref="Notes"/> are worth a look in the report but leave the task valid.
/// </summary>
internal sealed record OracleValues(IReadOnlyDictionary<string, JsonNode?> Values, IReadOnlyList<string> Problems, IReadOnlyList<string> Notes);

/// <summary>Runs the oracle calls of a task against eplanmcp, before the model answers (agent evals design 4.2).</summary>
internal static class OracleRunner
{
    /// <summary>
    /// <paramref name="tools"/> are the tools the server lists: a task naming any other is invalid. A pick with
    /// <c>[*]</c> collects its matches from every page; any other pick reads the first page, where totals are.
    /// </summary>
    public static async Task<OracleValues> RunAsync(EvalTask task, IToolClient client, IReadOnlySet<string> tools, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var problems = new List<string>();
        var notes = new List<string>();

        // A list and its total often come from the same call; it is made, with all its pages, once.
        var calls = new Dictionary<string, PagedOutcome>(StringComparer.Ordinal);
        foreach ((string name, OracleCall call) in task.Oracle)
        {
            if (!tools.Contains(call.Tool))
            {
                problems.Add($"oracle '{name}': {call.Tool} is not a tool of this eplanmcp");
                continue;
            }

            string key = call.Tool + " " + call.Args.ToJsonString();
            if (!calls.TryGetValue(key, out PagedOutcome? outcome))
            {
                calls[key] = outcome = await ToolPages.AllAsync(client, call.Tool, call.Args, cancellationToken).ConfigureAwait(false);
            }

            if (outcome.Problem is { } problem)
            {
                problems.Add($"oracle '{name}': {problem}");
                continue;
            }

            JsonNode? value;
            if (call.Pick.HasWildcard)
            {
                value = new JsonArray([.. outcome.Pages.SelectMany(page => call.Pick.TrySelect(page, out JsonNode? matches) ? matches!.AsArray().Select(m => m?.DeepClone()) : [])]);
            }
            else if (!call.Pick.TrySelect(outcome.Pages[0], out value))
            {
                problems.Add($"oracle '{name}': {call.Pick} matches nothing in the result of {call.Tool}");
                continue;
            }

            if (value is JsonArray { Count: 0 })
            {
                notes.Add($"oracle '{name}': {call.Pick} matched nothing, so the expected value is an empty list; check the path if that is not the answer");
            }

            values[name] = value?.DeepClone();
        }

        return new OracleValues(values, problems, notes);
    }
}
