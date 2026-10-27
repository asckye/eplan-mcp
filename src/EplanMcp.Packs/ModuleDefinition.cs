using System.Text.Json;

namespace EplanMcp.Packs;

public sealed record ModuleDefinition(JsonElement Spec, JsonElement ParameterSchema, RuleNode Applicability);
