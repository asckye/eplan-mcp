using System.Collections.Immutable;
using System.Text.Json;

namespace EplanMcp.Host.Plans;

internal sealed record PlanEngineering(
    string Cabinet,
    ImmutableArray<PlanConfigValue> ConfigValuesAndTrace,
    string ConfigDigest,
    JsonElement WorkflowRefAndInputs, // W1/W2 define the workflow reference and input contract.
    ImmutableArray<JsonElement> ModuleBindings, // M1/M8 define each module binding object.
    JsonElement RuleDigests, // R2/R4 define the rule digest evidence object.
    JsonElement Acceptance); // W1/M8 define the acceptance evidence object.

internal sealed record PlanConfigValue(
    string Key, JsonElement Value, string? Unit,
    JsonElement Constraints, // C2 defines the merged constraint object.
    ImmutableArray<PlanConfigTrace> Trace);

internal sealed record PlanConfigTrace(
    string Layer,
    JsonElement Source, // C2 defines exact resource/hash or binding revision evidence.
    string Pointer, string Kind, JsonElement Value);
