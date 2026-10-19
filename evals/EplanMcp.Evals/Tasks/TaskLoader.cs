using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EplanMcp.Evals.Scoring;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EplanMcp.Evals.Tasks;

/// <summary>One task file: the task when the file is valid, otherwise every problem found in it.</summary>
internal sealed record TaskFile(string Name, EvalTask? Task, IReadOnlyList<string> Problems);

/// <summary>
/// Reads the task files of agent evals design 4.1. A file is checked completely and every problem is reported with
/// its line, so one <c>check</c> run is enough to fix a file. Whether a tool is registered is checked against the
/// server's tool list once the runner talks to it (design step 4); here only the naming rule is.
/// </summary>
internal static partial class TaskLoader
{
    private const string OraclePrefix = "oracle.";

    private static readonly string[] _taskKeys = ["id", "set", "prompt", "answer", "oracle", "checks", "budget"];
    private static readonly string[] _oracleKeys = ["tool", "args", "pick"];
    private static readonly string[] _checkKeys = ["answer", .. Comparison.Names.Keys];
    private static readonly string[] _budgetKeys = ["max_tool_calls", "max_minutes", "max_usd"];

    /// <summary>Reads one file; <paramref name="name"/> is its name without <c>.yaml</c>, which the task id must repeat.</summary>
    public static TaskFile Load(string name, string yaml)
    {
        var reader = new Reader();
        EvalTask? task = null;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents is [{ RootNode: YamlMappingNode root }])
            {
                task = reader.Task(name, root);
            }
            else
            {
                reader.Problems.Add("line 1: a task file holds exactly one YAML mapping");
            }
        }
        catch (YamlException ex)
        {
            reader.Problems.Add($"line {ex.Start.Line}: {(ex.InnerException ?? ex).Message}");
        }

        return new TaskFile(name, reader.Problems.Count == 0 ? task : null, reader.Problems);
    }

    [GeneratedRegex(@"^eplan_[a-z]+_[a-z]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex ToolName();

    private sealed class Reader : YamlReader
    {
        private readonly Dictionary<string, (YamlNode Key, OracleCall? Call)> _oracle = new(StringComparer.Ordinal);
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public EvalTask? Task(string name, YamlMappingNode root)
        {
            Unknown(root, _taskKeys);
            YamlNode? idNode = Required(root, "id");
            if (Text(idNode, "'id'") is { } id && id != name)
            {
                Problem(idNode!, $"id '{id}' differs from the file name '{name}'");
            }

            YamlNode? setNode = Required(root, "set");
            string? setName = Text(setNode, "'set'");
            TaskSet? set = setName switch { "dev" => TaskSet.Dev, "holdout" => TaskSet.Holdout, _ => null };
            if (setName is not null && set is null)
            {
                Problem(setNode!, $"set '{setName}' is neither dev nor holdout");
            }

            string? prompt = Text(Required(root, "prompt"), "'prompt'")?.TrimEnd();
            JsonObject? answer = Required(root, "answer") is { } answerNode ? AnswerSchema(answerNode) : null;
            if (Child(root, "oracle") is { } oracleNode && Mapping(oracleNode, "'oracle'") is { } oracle)
            {
                ReadOracle(oracle);
            }

            List<Check> checks = Required(root, "checks") is { } checksNode ? Checks(checksNode, answer) : [];
            Budget budget = Child(root, "budget") is { } budgetNode && Mapping(budgetNode, "'budget'") is { } map ? BudgetOf(map) : Budget.Default;
            foreach ((string unused, (YamlNode key, _)) in _oracle.Where(o => !_used.Contains(o.Key)))
            {
                Problem(key, $"oracle '{unused}' is not used by any check");
            }

            return Problems.Count == 0
                ? new EvalTask(name, set!.Value, prompt!, answer!, _oracle.ToDictionary(o => o.Key, o => o.Value.Call!, StringComparer.Ordinal), checks, budget)
                : null;
        }

        private JsonObject? AnswerSchema(YamlNode node) =>
            Json(node) is JsonObject schema && IsString(schema["type"]) && (string?)schema["type"] == "object"
                ? schema
                : Reported<JsonObject>(node, "'answer' must be a JSON Schema of type object");

        private void ReadOracle(YamlMappingNode oracle)
        {
            foreach ((YamlNode key, YamlNode value) in oracle.Children)
            {
                if (Text(key, "an oracle name") is not { } name)
                {
                    continue;
                }

                OracleCall? call = null;
                if (Mapping(value, $"oracle '{name}'") is { } entry)
                {
                    Unknown(entry, _oracleKeys);
                    YamlNode? toolNode = Required(entry, "tool");
                    string? tool = Text(toolNode, "'tool'");
                    if (tool is not null && !ToolName().IsMatch(tool))
                    {
                        tool = Reported<string>(toolNode!, $"'{tool}' is not a tool name (eplan_<family>_<verb>)");
                    }

                    JsonObject? args = new JsonObject();
                    if (Child(entry, "args") is { } argsNode)
                    {
                        args = Mapping(argsNode, "'args'") is { } argsMap ? Json(argsMap) as JsonObject : null;
                    }

                    JsonPath? pick = PathOf(Required(entry, "pick"), "'pick'");
                    call = tool is not null && args is not null && pick is not null ? new OracleCall(tool, args, pick) : null;
                }

                _oracle[name] = (key, call);
            }
        }

        private List<Check> Checks(YamlNode node, JsonObject? answer)
        {
            if (node is not YamlSequenceNode { Children.Count: > 0 } sequence)
            {
                Problem(node, "'checks' must be a non-empty list");
                return [];
            }

            var checks = new List<Check>();
            foreach (YamlMappingNode check in sequence.Children.Select(item => Mapping(item, "a check")).OfType<YamlMappingNode>())
            {
                Unknown(check, _checkKeys);
                YamlNode? pathNode = Required(check, "answer");
                JsonPath? path = PathOf(pathNode, "'answer'");
                if (path?.FirstField is { } field && answer?["properties"] is JsonObject properties && !properties.ContainsKey(field))
                {
                    Problem(pathNode!, $"the answer schema has no property '{field}'");
                }

                KeyValuePair<YamlNode, YamlNode>[] comparisons = [.. check.Children.Where(c => c.Key is YamlScalarNode { Value: { } k } && Comparison.Names.ContainsKey(k))];
                if (comparisons is not [(YamlScalarNode { Value: { } kindName }, YamlNode expected)])
                {
                    Problem(check, $"a check needs exactly one of {string.Join(", ", Comparison.Names.Keys)}");
                    continue;
                }

                ComparisonKind kind = Comparison.Names[kindName];
                (string? oracleName, JsonNode? literal) = Expected(kindName, kind, expected);
                if (path is not null)
                {
                    checks.Add(new Check(path, kind, oracleName, literal));
                }
            }

            return checks;
        }

        /// <summary>A plain <c>oracle.&lt;name&gt;</c> refers to an oracle call; anything else, quoted text included, is a literal.</summary>
        private (string? Oracle, JsonNode? Literal) Expected(string kindName, ComparisonKind kind, YamlNode expected)
        {
            if (expected is YamlScalarNode { Style: ScalarStyle.Plain, Value: { } reference } && reference.StartsWith(OraclePrefix, StringComparison.Ordinal))
            {
                string name = reference[OraclePrefix.Length..];
                _used.Add(name);
                if (!_oracle.ContainsKey(name))
                {
                    Problem(expected, $"oracle '{name}' is not defined");
                }

                return (name, null);
            }

            if (expected is YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" })
            {
                // An empty value is YAML's null, but here it is far more likely a value left out; null is written out.
                Problem(expected, $"{kindName} has no expected value; write null to compare with null");
                return (null, null);
            }

            JsonNode? literal = Json(expected);
            if (Comparison.ExpectedProblem(kind, literal) is { } problem)
            {
                Problem(expected, $"{kindName} {problem}");
            }

            return (null, literal);
        }

        private Budget BudgetOf(YamlMappingNode budget)
        {
            Unknown(budget, _budgetKeys);
            decimal? calls = Limit(Child(budget, "max_tool_calls"), "max_tool_calls", 200, whole: true);
            decimal? minutes = Limit(Child(budget, "max_minutes"), "max_minutes", 60, whole: false);
            decimal? usd = Limit(Child(budget, "max_usd"), "max_usd", 5, whole: false);
            return new Budget(
                calls is { } c ? (int)c : Budget.Default.MaxToolCalls,
                minutes is { } m ? TimeSpan.FromMinutes((double)m) : Budget.Default.MaxDuration,
                usd ?? Budget.Default.MaxUsd);
        }

        /// <summary>A positive number up to <paramref name="max"/>; the upper bounds catch typos before they cost.</summary>
        private decimal? Limit(YamlNode? node, string key, decimal max, bool whole)
        {
            if (node is null)
            {
                return null;
            }

            if (Comparison.Number(Json(node)) is { } value && value > 0 && value <= max && (!whole || value == decimal.Truncate(value)))
            {
                return value;
            }

            Problem(node, $"'{key}' must be a positive {(whole ? "whole number" : "number")} up to {max}");
            return null;
        }

        private JsonPath? PathOf(YamlNode? node, string subject) => Text(node, subject) switch
        {
            null => null,
            var text when JsonPath.TryParse(text, out JsonPath? path) => path,
            var text => Reported<JsonPath>(node!, $"'{text}' is not a path of the supported subset ($, .field, [n], [*])"),
        };
    }
}
