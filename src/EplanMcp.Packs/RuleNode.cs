using System.Text.Json;

namespace EplanMcp.Packs;

/// <summary>A checked AST node; Value preserves data, never executable text.</summary>
public sealed record RuleNode(string Operator, RuleValueType Type, JsonElement Value, IReadOnlyList<RuleNode> Operands);
