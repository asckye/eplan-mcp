using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;

namespace EplanMcp.Evals.Tasks;

/// <summary>The development set improves the tool descriptions; the holdout set only measures them (testing.md 5).</summary>
internal enum TaskSet
{
    Dev,
    Holdout,
}

/// <summary>
/// The project a task works on (evals/README.md). Copies are fresh copies of the fixture, one per attempt, which the
/// prompt names as <see cref="EvalTask.ProjectToken"/>; the evals' eplanmcp lets the model write to the scratch copies
/// only (M3 design 7, Q4).
/// </summary>
internal enum TaskFixture
{
    /// <summary>The shared fixture, EPLAN's current project; writes to it are refused.</summary>
    Shared,

    /// <summary>A fresh copy in the evals' scratch folder: the model may write to it.</summary>
    Copy,

    /// <summary>A fresh copy outside the evals' scratch folder: every write to it is refused by the policy.</summary>
    CopyOutsideScratch,
}

/// <summary>
/// One task file after validation (agent evals design 4.1). <see cref="Answer"/> is the JSON Schema of the model's
/// answer, passed to <c>claude -p --json-schema</c>; <see cref="Oracle"/> holds the tool calls the runner makes itself
/// to learn the expected values, by name, before the attempt; <see cref="After"/> the calls it makes after the attempt,
/// to see what the model changed.
/// </summary>
internal sealed record EvalTask(
    string Id,
    TaskSet Set,
    string Prompt,
    JsonObject Answer,
    IReadOnlyDictionary<string, OracleCall> Oracle,
    IReadOnlyList<Check> Checks,
    Budget Budget)
{
    /// <summary>Where the prompt of a task on a copy names the copy; the runner puts the copy's project name there.</summary>
    public const string ProjectToken = "{project}";

    public TaskFixture Fixture { get; init; } = TaskFixture.Shared;

    public IReadOnlyDictionary<string, OracleCall> After { get; init; } = new Dictionary<string, OracleCall>();
}

/// <summary>A tool call whose structured result gives an expected value; <see cref="Pick"/> selects it.</summary>
internal sealed record OracleCall(string Tool, JsonObject Args, JsonPath Pick);

/// <summary>
/// Compares the answer value at <see cref="Answer"/>, or the value of the after call named <see cref="After"/>, with
/// the oracle value named <see cref="Oracle"/>, or with <see cref="Literal"/> when no oracle is named.
/// </summary>
internal sealed record Check(JsonPath? Answer, ComparisonKind Kind, string? Oracle, JsonNode? Literal, string? After = null)
{
    /// <summary>What the check looks at, as reports name it: the answer path or <c>after.&lt;name&gt;</c>.</summary>
    public string Subject => After is null ? Answer?.ToString() ?? "$" : "after." + After;
}

/// <summary>Limits of one attempt; going over any of them fails the task.</summary>
internal sealed record Budget(int MaxToolCalls, TimeSpan MaxDuration, decimal MaxUsd)
{
    /// <summary>The spending limit follows Q3 of the design and is confirmed after the first real run.</summary>
    public static Budget Default { get; } = new(20, TimeSpan.FromMinutes(5), 0.50m);
}
