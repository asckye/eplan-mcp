using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Server;

namespace EplanMcp.Evals.Tests.Server;

/// <summary>Answers tool calls from a script, in order per tool, and records what was asked.</summary>
internal sealed class FakeToolClient(params string[] tools) : IToolClient
{
    private readonly Dictionary<string, Queue<ToolOutcome>> _answers = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public FakeToolClient Answer(string tool, string structured) => Add(tool, new ToolOutcome(false, JsonNode.Parse(structured)!.AsObject(), "ok"));

    public FakeToolClient Fail(string tool, string text) => Add(tool, new ToolOutcome(true, null, text));

    public FakeToolClient Unstructured(string tool, string text) => Add(tool, new ToolOutcome(false, null, text));

    public Task<IReadOnlyList<string>> ListToolsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(tools);

    public Task<ToolOutcome> CallAsync(string tool, JsonObject args, CancellationToken cancellationToken)
    {
        Calls.Add($"{tool} {Comparison.Show(args)}");
        return _answers.TryGetValue(tool, out Queue<ToolOutcome>? queue) && queue.TryDequeue(out ToolOutcome? outcome)
            ? Task.FromResult(outcome)
            : throw new InvalidOperationException($"No answer scripted for {tool}.");
    }

    private FakeToolClient Add(string tool, ToolOutcome outcome)
    {
        if (!_answers.TryGetValue(tool, out Queue<ToolOutcome>? queue))
        {
            _answers[tool] = queue = new Queue<ToolOutcome>();
        }

        queue.Enqueue(outcome);
        return this;
    }
}
