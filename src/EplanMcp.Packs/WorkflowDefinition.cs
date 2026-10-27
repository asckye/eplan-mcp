using System.Text.Json;

namespace EplanMcp.Packs;

public sealed record WorkflowDefinition(JsonElement Spec, JsonElement ConfigSchema);
