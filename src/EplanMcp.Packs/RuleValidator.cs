using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using static EplanMcp.Packs.BindingReadModel;

namespace EplanMcp.Packs;

/// <summary>Static rule contracts only; evaluation and source confirmation are separate gates.</summary>
public static class RuleValidator
{
    public const int MaxDepth = 8;
    public const int MaxRelationHops = 3;
    // The design bounds runtime visits separately; bound stored AST nodes and set entries before evaluation.
    public const int MaxNodes = 1024;
    public const int MaxPatternLength = 256;
    public const int MaxInputLength = 2000;
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(20);
    public const RegexOptions PatternOptions = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
    public const RuleStatus MissingFactStatus = RuleStatus.Unknown;

    /// <summary>Only direct snapshot fields; absent values or incomplete coverage remain unknown.</summary>
    public static IReadOnlyDictionary<string, (RuleValueType Type, string Coverage)> Properties { get; } =
        new Dictionary<string, (RuleValueType, string)>(StringComparer.Ordinal)
        {
            ["function.name"] = (new("text"), "snapshot.name"),
            ["function.category"] = (new("text"), "snapshot.category"),
            ["function.main"] = (new("boolean"), "snapshot.main"),
            ["page.name"] = (new("text"), "snapshot.name"),
            ["page.type"] = (new("text"), "snapshot.pageType"),
            ["connection.name"] = (new("text"), "snapshot.name"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Reverse lookup also requires complete direct endpoint coverage; no electrical tracing.</summary>
    public static IReadOnlyDictionary<string, (string From, string To)> Relations { get; } =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["connection.start"] = ("connection", "function"),
            ["connection.end"] = ("connection", "function"),
            ["function.connections"] = ("function", "connection"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static (RuleNode Scope, RuleNode Check) Validate(JsonElement spec,
        Func<string, RuleValueType?>? config = null, Func<string, RuleValueType?>? parameter = null)
    {
        Object(spec, "/spec", "severity source scope check message", "fix");
        FiniteNumbers(spec, "/spec");
        Require(spec.GetProperty("source").ValueKind == JsonValueKind.Object && spec.GetProperty("source").EnumerateObject().Any() || Text(spec.GetProperty("source")), "/spec/source", "must name a source; authenticity and resolution are separate gates");
        Require(spec.GetProperty("severity").ValueKind == JsonValueKind.String && spec.GetProperty("severity").GetString() is "mandatory" or "recommended", "/spec/severity", "must be mandatory or recommended");
        JsonElement scope = spec.GetProperty("scope");
        Object(scope, "/spec/scope", "kind where", "");
        string kind = Text(scope.GetProperty("kind")) ? scope.GetProperty("kind").GetString()! : string.Empty;
        Require(kind is "function" or "page" or "connection", "/spec/scope/kind", "must be a covered object kind");
        JsonElement message = spec.GetProperty("message");
        Require(message.ValueKind == JsonValueKind.Object && message.EnumerateObject().Any(), "/spec/message", "must contain language texts");
        foreach (JsonProperty text in message.EnumerateObject())
        {
            Require(Text(text.Value) && text.Value.GetString()!.Length <= MaxInputLength, Child("/spec/message", text.Name), "must be nonempty text of at most 2000 characters");
        }

        int nodes = 0;
        RuleNode where = Read(scope.GetProperty("where"), "/spec/scope/where", kind, 1, 0);
        RuleNode check = Read(spec.GetProperty("check"), "/spec/check", kind, 1, 0);
        Require(where.Type == new RuleValueType("boolean"), "/spec/scope/where", "must be a predicate");
        Require(check.Type == new RuleValueType("boolean"), "/spec/check", "must be a predicate");
        if (spec.TryGetProperty("fix", out JsonElement fix))
        {
            Object(fix, "/spec/fix", "workflow inputs", "");
            Require(Text(fix.GetProperty("workflow")), "/spec/fix/workflow", "must name a workflow; reference resolution is a separate gate");
            JsonElement inputs = fix.GetProperty("inputs");
            Require(inputs.ValueKind == JsonValueKind.Object, "/spec/fix/inputs", "must contain explicit input nodes");
            foreach (JsonProperty input in inputs.EnumerateObject())
            {
                string at = Child("/spec/fix/inputs", input.Name);
                if (input.Value.ValueKind == JsonValueKind.Object && input.Value.TryGetProperty("object", out JsonElement current))
                {
                    Object(input.Value, at, "object", "");
                    Require(current.ValueKind == JsonValueKind.True && ++nodes <= MaxNodes, at + "/object", "must be the current object within the node limit");
                }
                else
                {
                    _ = Read(input.Value, at, kind, 1, 0);
                }
            }
        }

        return (where, check);

        RuleNode Read(JsonElement node, string at, string owner, int depth, int hops)
        {
            Require(depth <= MaxDepth && ++nodes <= MaxNodes, at, "exceeds AST depth 8 or node/set-entry limit 1024");
            Object(node, at, "", "property config parameter literal unit eq ne lt le gt ge in not_in exists empty starts_with ends_with matches all any not some every count");
            JsonProperty[] members = [.. node.EnumerateObject().Where(p => p.Name != "unit")];
            Require(members.Length == 1, at, "must contain exactly one explicit operand or operator");
            (string op, JsonElement value) = (members[0].Name, members[0].Value);
            string path = Child(at, op);
            bool hasUnit = node.TryGetProperty("unit", out JsonElement unit);
            Require(!hasUnit || op == "literal" && Text(unit) && ConfigValidator.Units.ContainsKey(unit.GetString()!), at + "/unit", "requires a numeric literal and a registered unit");
            RuleValueType type;
            RuleNode[] children = [];
            if (op == "literal")
            {
                bool set = value.ValueKind == JsonValueKind.Array;
                JsonElement[] values = set ? [.. value.EnumerateArray()] : [value];
                nodes += set ? values.Length : 0;
                Require(values.Length > 0 && nodes <= MaxNodes, path, "must be a nonempty bounded scalar or homogeneous set");
                string scalar = values[0].ValueKind switch { JsonValueKind.String => "text", JsonValueKind.Number => "number", JsonValueKind.True or JsonValueKind.False => "boolean", _ => "invalid" };
                Require(scalar != "invalid" && values.All(v => Scalar(v) && (scalar == "boolean" ? v.ValueKind is JsonValueKind.True or JsonValueKind.False : v.ValueKind == values[0].ValueKind)), path, "must contain one scalar type without coercion");
                Require(!hasUnit || scalar == "number", at + "/unit", "units require numbers");
                type = new(scalar, hasUnit ? unit.GetString() : null, set);
            }
            else if (op is "property" or "config" or "parameter")
            {
                Require(Text(value), path, "must name a registered operand");
                string name = value.GetString()!;
                RuleValueType? found = op == "property" ? Properties.TryGetValue(name, out var fact) && name.StartsWith(owner + ".", StringComparison.Ordinal) ? fact.Type : null
                    : op == "config" ? config?.Invoke(name) : parameter?.Invoke(name);
                Require(found.HasValue, path, "is not registered in this context");
                type = found!.Value;
                Require(type.Kind is "text" or "number" or "boolean" && (type.Unit is null || type.Kind == "number" && ConfigValidator.Units.ContainsKey(type.Unit)), path, "has an invalid registered type or unit");
            }
            else if (op is "some" or "every" or "count")
            {
                Object(value, path, "relation where", "");
                JsonElement relation = value.GetProperty("relation");
                Require(Text(relation) && Relations.ContainsKey(relation.GetString()!), path + "/relation", "relation has no registered direct snapshot coverage");
                (string from, string to) = Relations[relation.GetString()!];
                Require(from == owner && hops < MaxRelationHops, path + "/relation", "requires the relation's source kind and at most 3 hops");
                children = [Read(value.GetProperty("where"), path + "/where", to, depth + 1, hops + 1)];
                Require(children[0].Type == new RuleValueType("boolean"), path + "/where", "must be a predicate");
                type = new(op == "count" ? "number" : "boolean");
            }
            else
            {
                Require(value.ValueKind == JsonValueKind.Array, path, "must be an explicit operand array");
                int arity = op is "not" or "exists" or "empty" ? 1 : 2;
                Require(op is "all" or "any" ? value.GetArrayLength() > 0 : value.GetArrayLength() == arity, path, "has the wrong operand count");
                children = [.. value.EnumerateArray().Select((v, i) => Read(v, $"{path}/{i}", owner, depth + 1, hops))];
                RuleValueType left = children[0].Type;
                RuleValueType right = children.Length > 1 ? children[1].Type : left;
                bool compatible = left.Kind == right.Kind && Dimension(left) == Dimension(right);
                Require(op switch
                {
                    "all" or "any" or "not" => children.All(c => c.Type == new RuleValueType("boolean")),
                    "exists" => true,
                    "empty" => left.Set || left.Kind == "text",
                    "in" or "not_in" => !left.Set && right.Set && compatible,
                    "eq" or "ne" => !left.Set && !right.Set && compatible,
                    "lt" or "le" or "gt" or "ge" => !left.Set && !right.Set && compatible && left.Kind == "number",
                    _ => left == new RuleValueType("text") && right == left,
                }, path, "operand types or unit dimensions do not match the operator");
                if (op == "matches")
                {
                    string patternAt = path + "/1";
                    Require(children[1].Operator == "literal" && children[1].Value.GetString()!.Length <= MaxPatternLength, patternAt, "requires a literal pattern of at most 256 characters");
                    try
                    {
                        _ = new Regex(children[1].Value.GetString()!, PatternOptions, RegexTimeout);
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                    {
                        throw new PackFormatException(patternAt + ": must be a valid NonBacktracking regex", ex);
                    }

                    Require(children[0].Operator != "literal" || children[0].Value.GetString()!.Length <= MaxInputLength, path + "/0", "regex input exceeds 2000 characters");
                }

                type = new("boolean");
            }

            return new RuleNode(op, type, value.Clone(), Array.AsReadOnly(children));
        }
    }

    private static string? Dimension(RuleValueType type) => type.Unit is null ? null : ConfigValidator.Units[type.Unit].Dimension;

    private static bool Text(JsonElement node) => node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(node.GetString());

    private static bool Scalar(JsonElement node) => node.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;
}
