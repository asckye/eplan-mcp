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
    private const string AfterPrefix = "after.";

    private static readonly string[] _taskKeys = ["id", "set", "fixture", "prompt", "answer", "oracle", "after", "checks", "budget"];
    private static readonly string[] _oracleKeys = ["tool", "args", "pick"];
    private static readonly string[] _checkKeys = ["answer", "after", .. Comparison.Names.Keys];

    private static readonly Dictionary<string, TaskFixture> _fixtures = new(StringComparer.Ordinal)
    {
        ["shared"] = TaskFixture.Shared,
        ["copy"] = TaskFixture.Copy,
        ["copy_outside_scratch"] = TaskFixture.CopyOutsideScratch,
    };
    private static readonly string[] _budgetKeys = ["max_tool_calls", "max_minutes", "max_usd"];

    public static IReadOnlyList<TaskFile> LoadDirectory(string directory) =>
        [.. Directory.EnumerateFiles(directory, "*.yaml").Order(StringComparer.Ordinal)
            .Select(file => Load(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)))];

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
        private readonly Dictionary<string, (YamlNode Key, OracleCall? Call)> _after = new(StringComparer.Ordinal);
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        private readonly HashSet<string> _usedAfter = new(StringComparer.Ordinal);

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

            TaskFixture fixture = FixtureOf(Child(root, "fixture"));
            YamlNode? promptNode = Required(root, "prompt");
            string? prompt = Text(promptNode, "'prompt'")?.TrimEnd();
            if (prompt is not null && prompt.Contains(EvalTask.ProjectToken, StringComparison.Ordinal) != (fixture != TaskFixture.Shared))
            {
                Problem(promptNode!, fixture == TaskFixture.Shared
                    ? $"only a task on a copy (fixture: copy) names its project as {EvalTask.ProjectToken}"
                    : $"the prompt must name the copy as {EvalTask.ProjectToken}");
            }

            JsonObject? answer = Required(root, "answer") is { } answerNode ? AnswerSchema(answerNode) : null;
            if (Child(root, "oracle") is { } oracleNode && Mapping(oracleNode, "'oracle'") is { } oracle)
            {
                ReadCalls(oracle, _oracle, "oracle");
            }

            if (Child(root, "after") is { } afterNode && Mapping(afterNode, "'after'") is { } after)
            {
                ReadCalls(after, _after, "after");
            }

            List<Check> checks = Required(root, "checks") is { } checksNode ? Checks(checksNode, answer) : [];
            Budget budget = Child(root, "budget") is { } budgetNode && Mapping(budgetNode, "'budget'") is { } map ? BudgetOf(map) : Budget.Default;
            foreach ((string unused, (YamlNode key, _)) in _oracle.Where(o => !_used.Contains(o.Key)))
            {
                Problem(key, $"oracle '{unused}' is not used by any check");
            }

            foreach ((string unused, (YamlNode key, _)) in _after.Where(o => !_usedAfter.Contains(o.Key)))
            {
                Problem(key, $"after '{unused}' is not checked by any check");
            }

            return Problems.Count == 0
                ? new EvalTask(name, set!.Value, prompt!, answer!, Calls(_oracle), checks, budget) { Fixture = fixture, After = Calls(_after) }
                : null;
        }

        private static Dictionary<string, OracleCall> Calls(Dictionary<string, (YamlNode Key, OracleCall? Call)> calls) =>
            calls.ToDictionary(o => o.Key, o => o.Value.Call!, StringComparer.Ordinal);

        private TaskFixture FixtureOf(YamlNode? node)
        {
            if (node is null)
            {
                return TaskFixture.Shared;
            }

            string? text = Text(node, "'fixture'");
            if (text is not null && _fixtures.TryGetValue(text, out TaskFixture fixture))
            {
                return fixture;
            }

            if (text is not null)
            {
                Problem(node, $"fixture '{text}' is not one of {string.Join(", ", _fixtures.Keys)}");
            }

            return TaskFixture.Shared;
        }

        private JsonObject? AnswerSchema(YamlNode node)
        {
            if (Json(node) is not JsonObject schema || !IsString(schema["type"]) || (string?)schema["type"] != "object")
            {
                return Reported<JsonObject>(node, "'answer' must be a JSON Schema of type object");
            }

            EmptyKeys(node, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance));
            return schema;
        }

        /// <summary>
        /// A key without a value in the answer schema is a slip: in a { } mapping, ", " inside an unquoted string starts
        /// a new key, so <c>{ description: the tag, such as -K1 }</c> also has the key <c>such as -K1</c>. claude rejects
        /// such a schema and every attempt comes out invalid (first real run). An explicit <c>null</c> or <c>~</c> is meant
        /// and passes; <paramref name="seen"/> stops at aliases met before.
        /// </summary>
        private void EmptyKeys(YamlNode node, HashSet<YamlNode> seen)
        {
            if (!seen.Add(node))
            {
                return;
            }

            IEnumerable<YamlNode> children = node switch
            {
                YamlMappingNode map => map.Children.Values,
                YamlSequenceNode sequence => sequence.Children,
                _ => [],
            };
            if (node is YamlMappingNode mapping)
            {
                foreach ((YamlNode key, _) in mapping.Children.Where(c => c.Value is YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" }))
                {
                    Problem(key, $"'{(key as YamlScalarNode)?.Value}' in 'answer' has no value; quote a string that contains ', ' inside {{ }}");
                }
            }

            foreach (YamlNode child in children)
            {
                EmptyKeys(child, seen);
            }
        }

        /// <summary>The oracle calls, or the after calls: <paramref name="kind"/> names them in problems.</summary>
        private void ReadCalls(YamlMappingNode calls, Dictionary<string, (YamlNode Key, OracleCall? Call)> target, string kind)
        {
            foreach ((YamlNode key, YamlNode value) in calls.Children)
            {
                if (Text(key, $"an {kind} name") is not { } name)
                {
                    continue;
                }

                OracleCall? call = null;
                if (Mapping(value, $"{kind} '{name}'") is { } entry)
                {
                    Unknown(entry, _oracleKeys);
                    YamlNode? toolNode = Required(entry, "tool");
                    string? tool = Text(toolNode, "'tool'");
                    bool exported = tool == EvalTask.ExportedFiles;
                    if (exported && kind != "after")
                    {
                        tool = Reported<string>(toolNode!, $"{EvalTask.ExportedFiles} is an after call: the files are there once the attempt is over");
                    }
                    else if (tool is not null && !exported && !ToolName().IsMatch(tool))
                    {
                        tool = Reported<string>(toolNode!, $"'{tool}' is not a tool name (eplan_<family>_<verb>)");
                    }

                    JsonObject? args = new JsonObject();
                    if (Child(entry, "args") is { } argsNode)
                    {
                        args = Mapping(argsNode, "'args'") is { } argsMap ? Json(argsMap) as JsonObject : null;
                        if (exported && args is not null && args.Any(a => a.Key != "extension" || !IsString(a.Value)))
                        {
                            args = Reported<JsonObject>(argsNode, $"{EvalTask.ExportedFiles} takes one argument only, a string 'extension' such as pdf");
                        }
                    }

                    JsonPath? pick = PathOf(Required(entry, "pick"), "'pick'");
                    call = tool is not null && args is not null && pick is not null ? new OracleCall(tool, args, pick) : null;
                }

                target[name] = (key, call);
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
                (bool subject, JsonPath? path, string? after) = Subject(check, answer);
                KeyValuePair<YamlNode, YamlNode>[] comparisons = [.. check.Children.Where(c => c.Key is YamlScalarNode { Value: { } k } && Comparison.Names.ContainsKey(k))];
                if (comparisons is not [(YamlScalarNode { Value: { } kindName }, YamlNode expected)])
                {
                    Problem(check, $"a check needs exactly one of {string.Join(", ", Comparison.Names.Keys)}");
                    continue;
                }

                ComparisonKind kind = Comparison.Names[kindName];
                (string? oracleName, string? expectedAfter, JsonNode? literal) = Expected(kindName, kind, expected);
                if (subject)
                {
                    checks.Add(new Check(path, kind, oracleName, literal, after, expectedAfter));
                }
            }

            return checks;
        }

        /// <summary>What a check looks at: a path into the answer, or the value of an after call; exactly one of them.</summary>
        private (bool Valid, JsonPath? Path, string? After) Subject(YamlMappingNode check, JsonObject? answer)
        {
            YamlNode? pathNode = Child(check, "answer");
            YamlNode? afterNode = Child(check, "after");
            if ((pathNode is null) == (afterNode is null))
            {
                Problem(check, "a check needs exactly one of 'answer' (a path into the answer) or 'after' (the name of an after call)");
                return (false, null, null);
            }

            if (afterNode is not null)
            {
                string? after = Text(afterNode, "'after'");
                if (after is not null && !_after.ContainsKey(after))
                {
                    Problem(afterNode, $"after '{after}' is not defined");
                }

                _usedAfter.Add(after ?? string.Empty);
                return (after is not null, null, after);
            }

            JsonPath? path = PathOf(pathNode, "'answer'");
            if (path?.FirstField is { } field && answer?["properties"] is JsonObject properties && !properties.ContainsKey(field))
            {
                Problem(pathNode!, $"the answer schema has no property '{field}'");
            }

            return (path is not null, path, null);
        }

        /// <summary>
        /// A plain <c>oracle.&lt;name&gt;</c> refers to an oracle call, a plain <c>after.&lt;name&gt;</c> to an after call;
        /// anything else, quoted text included, is a literal.
        /// </summary>
        private (string? Oracle, string? After, JsonNode? Literal) Expected(string kindName, ComparisonKind kind, YamlNode expected)
        {
            if (expected is YamlScalarNode { Style: ScalarStyle.Plain, Value: { } reference } && reference.StartsWith(OraclePrefix, StringComparison.Ordinal))
            {
                string name = reference[OraclePrefix.Length..];
                _used.Add(name);
                if (!_oracle.ContainsKey(name))
                {
                    Problem(expected, $"oracle '{name}' is not defined");
                }

                return (name, null, null);
            }

            if (expected is YamlScalarNode { Style: ScalarStyle.Plain, Value: { } afterReference } && afterReference.StartsWith(AfterPrefix, StringComparison.Ordinal))
            {
                string name = afterReference[AfterPrefix.Length..];
                _usedAfter.Add(name);
                if (!_after.ContainsKey(name))
                {
                    Problem(expected, $"after '{name}' is not defined");
                }

                return (null, name, null);
            }

            if (expected is YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" })
            {
                // An empty value is YAML's null, but here it is far more likely a value left out; null is written out.
                Problem(expected, $"{kindName} has no expected value; write null to compare with null");
                return (null, null, null);
            }

            JsonNode? literal = Json(expected);
            if (Comparison.ExpectedProblem(kind, literal) is { } problem)
            {
                Problem(expected, $"{kindName} {problem}");
            }

            return (null, null, literal);
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
