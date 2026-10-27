using System.Collections.Immutable;
using System.Text.Json;

namespace EplanMcp.Packs;

public sealed record WorkflowStep(string Op, JsonElement Args, ImmutableArray<int> DependsOn);
