using System.Text.Json.Nodes;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Scoring;

/// <summary>
/// The verdict on one answer: <see cref="Failures"/> lists the failed checks; <see cref="Invalid"/> the checks whose
/// oracle value their comparison cannot use. Those are mistakes of the task, so they make it invalid rather than
/// failed (agent evals design 4.2).
/// </summary>
internal sealed record Verdict(IReadOnlyList<string> Failures, IReadOnlyList<string> Invalid)
{
    public bool Solved => Failures.Count == 0 && Invalid.Count == 0;
}

/// <summary>Runs the checks of a task on the model's structured answer (agent evals design 4.2, step 5).</summary>
internal static class Grader
{
    /// <summary>
    /// Grades <paramref name="answer"/>, one line per failed or invalid check. <paramref name="oracle"/> holds the
    /// expected value of every oracle call of the task, by name.
    /// </summary>
    public static Verdict Grade(EvalTask task, JsonNode? answer, IReadOnlyDictionary<string, JsonNode?> oracle)
    {
        var failures = new List<string>();
        var invalid = new List<string>();
        foreach (Check check in task.Checks)
        {
            string name = Comparison.Names.First(n => n.Value == check.Kind).Key;
            JsonNode? expected = check.Oracle is { } oracleName ? oracle[oracleName] : check.Literal;
            if (check.Oracle is not null && Comparison.ExpectedProblem(check.Kind, expected) is { } problem)
            {
                invalid.Add($"{check.Answer} {name}: oracle '{check.Oracle}' gave {Comparison.Show(expected)}, but {name} {problem}");
            }
            else if (!check.Answer.TrySelect(answer, out JsonNode? actual))
            {
                failures.Add($"{check.Answer} {name}: the answer has no value here");
            }
            else if (Comparison.Fail(check.Kind, actual, expected) is { } why)
            {
                failures.Add($"{check.Answer} {name}: {why}");
            }
        }

        return new Verdict(failures, invalid);
    }
}
