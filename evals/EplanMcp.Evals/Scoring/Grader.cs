using System.Text.Json.Nodes;
using EplanMcp.Evals.Server;
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

/// <summary>Runs the checks of a task on the model's structured answer and on what the after calls found (design 4.2, step 5).</summary>
internal static class Grader
{
    /// <summary>
    /// Grades <paramref name="answer"/> and the after calls, one line per failed or invalid check. <paramref name="oracle"/>
    /// holds the expected value of every oracle call of the task, by name; <paramref name="after"/> what every after call
    /// found, by name, which a check may also expect (<c>after.&lt;name&gt;</c>), such as the error count a model reports.
    /// </summary>
    public static Verdict Grade(EvalTask task, JsonNode? answer, IReadOnlyDictionary<string, JsonNode?> oracle, IReadOnlyDictionary<string, AfterValue>? after = null)
    {
        var failures = new List<string>();
        var invalid = new List<string>();
        foreach (Check check in task.Checks)
        {
            string name = Comparison.Names.First(n => n.Value == check.Kind).Key;
            JsonNode? expected = check.Oracle is { } oracleName ? oracle[oracleName] : check.Literal;
            string? unusable = null;
            if (check.ExpectedAfter is { } expectedAfter)
            {
                // What an after call found depends on the model, like the subject: a value it cannot give fails the check.
                unusable = Found(after, expectedAfter, out expected) is { } absent ? $"the expected after.{expectedAfter}: {absent}"
                    : Comparison.ExpectedProblem(check.Kind, expected) is { } wrong ? $"after.{expectedAfter} gave {Comparison.Show(expected)}, but {name} {wrong}"
                    : null;
            }

            if (check.Oracle is not null && Comparison.ExpectedProblem(check.Kind, expected) is { } problem)
            {
                invalid.Add($"{check.Subject} {name}: oracle '{check.Oracle}' gave {Comparison.Show(expected)}, but {name} {problem}");
            }
            else if (unusable is not null)
            {
                failures.Add($"{check.Subject} {name}: {unusable}");
            }
            else if (Actual(check, answer, after, out JsonNode? actual) is { } missing)
            {
                failures.Add($"{check.Subject} {name}: {missing}");
            }
            else if (Comparison.Fail(check.Kind, actual, expected) is { } why)
            {
                failures.Add($"{check.Subject} {name}: {why}");
            }
        }

        return new Verdict(failures, invalid);
    }

    /// <summary>The value a check looks at in <paramref name="actual"/>, or why there is none.</summary>
    private static string? Actual(Check check, JsonNode? answer, IReadOnlyDictionary<string, AfterValue>? after, out JsonNode? actual) =>
        check.After is { } name ? Found(after, name, out actual)
            : check.Answer!.TrySelect(answer, out actual) ? null
            : "the answer has no value here";

    /// <summary>The value the after call <paramref name="name"/> found, or why there is none.</summary>
    private static string? Found(IReadOnlyDictionary<string, AfterValue>? after, string name, out JsonNode? value)
    {
        value = null;
        if (after?.GetValueOrDefault(name) is not { } found)
        {
            return "the after call was not made";
        }

        value = found.Value;
        return found.Missing;
    }
}
