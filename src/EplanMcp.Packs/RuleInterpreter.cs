using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EplanMcp.Packs;

/// <summary>Pure bounded evaluation; all mutable state belongs to one evaluation.</summary>
public sealed class RuleInterpreter(TimeProvider? timeProvider = null)
{
    private static readonly object _absent = new();
    public const int MaxRules = 500;
    public const int MaxVisits = 100_000;
    public static readonly TimeSpan RuleTimeout = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan EvaluationTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    internal int VisitLimit { get; init; } = MaxVisits;
    internal Func<string, string, bool> Match { get; init; } = (input, pattern) => Regex.IsMatch(input, pattern, RuleValidator.PatternOptions, RuleValidator.RegexTimeout);

    public Task<IReadOnlyList<RuleResult>> EvaluateAsync(IReadOnlyList<RuleDefinition> rules, IRuleFactSnapshot facts,
        IReadOnlyDictionary<string, RuleValue> config, IReadOnlyDictionary<string, RuleValue> parameters, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<RuleResult>>(() => Evaluate(rules, facts, config, parameters, cancellationToken), cancellationToken);

    private ReadOnlyCollection<RuleResult> Evaluate(IReadOnlyList<RuleDefinition> rules, IRuleFactSnapshot facts,
        IReadOnlyDictionary<string, RuleValue> config, IReadOnlyDictionary<string, RuleValue> parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long started = _clock.GetTimestamp();
        var results = new List<RuleResult>();
        bool totalExceeded = false;
        foreach (RuleDefinition rule in rules)
        {
            long ruleStarted = _clock.GetTimestamp();
            int visits = 0, first = results.Count;
            string? exhausted = rules.Count > MaxRules ? "Cabinet rule limit exceeded." : null;
            foreach (string locator in facts.Objects(rule.Kind))
            {
                var evidence = new List<RuleEvidence>();
                bool inferred = false;
                object? scope = Read(rule.Scope, locator, "/spec/scope/where");
                RuleStatus status = scope is false ? RuleStatus.NotApplicable : scope is true
                    ? Read(rule.Check, locator, "/spec/check") switch { true => RuleStatus.Pass, false => RuleStatus.Fail, _ => RuleStatus.Unknown }
                    : RuleStatus.Unknown;
                _ = WithinBudget();
                results.Add(new(rule.Rule, rule.Version, locator, status, evidence.AsReadOnly(), inferred ? "inference" : "fact"));

                object? Unknown(string at, string owner, string note)
                {
                    evidence.Add(new(at, owner, null, false, note));
                    return null;
                }

                object? Read(RuleNode node, string owner, string at)
                {
                    if (!WithinBudget() || ++visits > VisitLimit)
                    {
                        exhausted ??= "Rule node visit limit exceeded.";
                        return null;
                    }

                    string op = node.Operator;
                    string path = BindingReadModel.Child(at, op);
                    if (op == "literal")
                    {
                        return Value(node.Value, node.Type, path, owner);
                    }

                    if (op is "property" or "config" or "parameter")
                    {
                        string name = node.Value.GetString()!;
                        RuleValue? value = op == "property" ? facts.GetProperty(owner, name) : (op == "config" ? config : parameters).GetValueOrDefault(name);
                        inferred |= value?.Inferred == true;
                        if (value is { Complete: true, Value.ValueKind: JsonValueKind.Null } && value.Type == node.Type)
                        {
                            // Complete coverage with no value is a known absence, not missing data: exists=false, empty=true.
                            evidence.Add(new(path, owner, null, true, value.Evidence ?? "Known absent with complete coverage."));
                            return _absent;
                        }

                        bool covered = value is { Complete: true } && value.Type == node.Type;
                        evidence.Add(new(path, owner, value?.Value is { ValueKind: not JsonValueKind.Undefined } data ? data : null, covered, value?.Evidence ?? (covered ? null : "Missing value or incomplete/type-mismatched coverage.")));
                        return covered ? Value(value!.Value, value.Type, path, owner) : null;
                    }

                    if (op is "some" or "every" or "count")
                    {
                        RuleRelation relation = facts.Relation(owner, node.Value.GetProperty("relation").GetString()!);
                        inferred |= relation.Inferred;
                        evidence.Add(new(path + "/relation", owner, null, relation.Complete, relation.Evidence));
                        if (!relation.Complete)
                        {
                            return Unknown(path, owner, "Incomplete relation coverage.");
                        }

                        bool unknown = false;
                        decimal count = 0;
                        foreach (string target in relation.Locators)
                        {
                            object? match = Read(node.Operands[0], target, path + "/where");
                            if (exhausted is not null)
                            {
                                return null;
                            }

                            if (op == "some" && match is true || op == "every" && match is false)
                            {
                                return op == "some";
                            }

                            unknown |= match is null;
                            count += match is true ? 1 : 0;
                        }

                        return unknown ? null : op == "count" ? count : op == "every";
                    }

                    if (op is "all" or "any")
                    {
                        bool unknown = false;
                        for (int i = 0; i < node.Operands.Count; i++)
                        {
                            object? child = Read(node.Operands[i], owner, $"{path}/{i}");
                            if (exhausted is not null)
                            {
                                return null;
                            }

                            if (op == "all" && child is false || op == "any" && child is true)
                            {
                                return op == "any";
                            }

                            unknown |= child is null;
                        }

                        return unknown ? null : op == "all";
                    }

                    object? left = Read(node.Operands[0], owner, path + "/0");
                    if (left is null)
                    {
                        return null;
                    }

                    if (ReferenceEquals(left, _absent))
                    {
                        return op switch { "exists" => false, "empty" => true, _ => Unknown(path, owner, "Comparison with a known-absent value.") };
                    }

                    if (op is "not" or "exists" or "empty")
                    {
                        return op == "not" ? !(bool)left : op == "exists" || (left is string text ? text.Length == 0 : ((object[])left).Length == 0);
                    }

                    object? right = Read(node.Operands[1], owner, path + "/1");
                    if (right is null || ReferenceEquals(right, _absent))
                    {
                        return ReferenceEquals(right, _absent) ? Unknown(path, owner, "Comparison with a known-absent value.") : null;
                    }

                    if (op == "matches")
                    {
                        if (((string)left).Length > RuleValidator.MaxInputLength)
                        {
                            return Unknown(path, owner, "Regex input limit exceeded.");
                        }

                        try
                        {
                            return Match((string)left, (string)right);
                        }
                        catch (RegexMatchTimeoutException)
                        {
                            return Unknown(path, owner, "Regex timeout exceeded.");
                        }
                    }

                    return op switch
                    {
                        "in" or "not_in" => ((object[])right).Contains(left) == (op == "in"),
                        "eq" => Equals(left, right),
                        "ne" => !Equals(left, right),
                        "lt" => (decimal)left < (decimal)right,
                        "le" => (decimal)left <= (decimal)right,
                        "gt" => (decimal)left > (decimal)right,
                        "ge" => (decimal)left >= (decimal)right,
                        "starts_with" => ((string)left).StartsWith((string)right, StringComparison.Ordinal),
                        "ends_with" => ((string)left).EndsWith((string)right, StringComparison.Ordinal),
                        _ => throw new InvalidOperationException("Expected a validated rule operator."),
                    };
                }

                object? Value(JsonElement json, RuleValueType type, string at, string owner)
                {
                    if (type.Set && json.ValueKind == JsonValueKind.Array)
                    {
                        var values = new List<object>();
                        foreach (JsonElement item in json.EnumerateArray())
                        {
                            if (!WithinBudget() || ++visits > VisitLimit)
                            {
                                exhausted ??= "Rule node visit limit exceeded.";
                                return null;
                            }

                            object? value = Value(item, type with { Set = false }, at, owner);
                            if (value is null)
                            {
                                return null;
                            }

                            values.Add(value);
                        }

                        return values.ToArray();
                    }

                    if (!type.Set && type.Kind == "text" && json.ValueKind == JsonValueKind.String)
                    {
                        return json.GetString();
                    }

                    if (!type.Set && type.Kind == "boolean" && json.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        return json.GetBoolean();
                    }

                    if (!type.Set && type.Kind == "number" && json.ValueKind == JsonValueKind.Number && json.TryGetDecimal(out decimal number))
                    {
                        decimal factor = type.Unit is null ? 1 : ConfigValidator.Units[type.Unit].Factor;
                        try
                        {
                            decimal converted = number * factor;
                            if (converted / factor == number)
                            {
                                return converted;
                            }
                        }
                        catch (OverflowException)
                        {
                            return Unknown(at, owner, "Unit conversion exceeds finite decimal range.");
                        }
                    }

                    return Unknown(at, owner, "Value type or finite unit precision is unavailable.");
                }
            }

            _ = WithinBudget();
            if (exhausted is not null)
            {
                for (int i = first; i < results.Count; i++)
                {
                    results[i] = Uncovered(results[i], exhausted, cancellationToken);
                }
            }

            bool WithinBudget()
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalExceeded |= _clock.GetElapsedTime(started) >= EvaluationTimeout;
                exhausted ??= totalExceeded ? "Evaluation time limit exceeded." : _clock.GetElapsedTime(ruleStarted) >= RuleTimeout ? "Rule time limit exceeded." : null;
                return exhausted is null;
            }
        }

        return (totalExceeded ? results.Select(r => Uncovered(r, "Evaluation time limit exceeded.", cancellationToken)).ToList() : results).AsReadOnly();
    }

    private static RuleResult Uncovered(RuleResult result, string note, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return result with
        {
            Status = RuleStatus.Unknown,
            Evidence = Array.AsReadOnly<RuleEvidence>([.. result.Evidence, new("", result.Locator, null, false, note)]),
        };
    }
}
