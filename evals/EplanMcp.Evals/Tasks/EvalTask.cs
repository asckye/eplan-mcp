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
/// One task file after validation (agent evals design 4.1). <see cref="Answer"/> is the JSON Schema of the model's
/// answer, passed to <c>claude -p --json-schema</c>; <see cref="Oracle"/> holds the tool calls the runner makes itself
/// to learn the expected values, by name.
/// </summary>
internal sealed record EvalTask(
    string Id,
    TaskSet Set,
    string Prompt,
    JsonObject Answer,
    IReadOnlyDictionary<string, OracleCall> Oracle,
    IReadOnlyList<Check> Checks,
    Budget Budget);

/// <summary>A tool call whose structured result gives an expected value; <see cref="Pick"/> selects it.</summary>
internal sealed record OracleCall(string Tool, JsonObject Args, JsonPath Pick);

/// <summary>
/// Compares the answer value at <see cref="Answer"/> with the oracle value named <see cref="Oracle"/>, or with
/// <see cref="Literal"/> when no oracle is named.
/// </summary>
internal sealed record Check(JsonPath Answer, ComparisonKind Kind, string? Oracle, JsonNode? Literal);

/// <summary>Limits of one attempt; going over any of them fails the task.</summary>
internal sealed record Budget(int MaxToolCalls, TimeSpan MaxDuration, decimal MaxUsd)
{
    /// <summary>The spending limit follows Q3 of the design and is confirmed after the first real run.</summary>
    public static Budget Default { get; } = new(20, TimeSpan.FromMinutes(5), 0.50m);
}
