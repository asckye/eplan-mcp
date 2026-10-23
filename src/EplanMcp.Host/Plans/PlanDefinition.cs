using System.Collections.Immutable;
using System.Text.Json;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Plans;

/// <summary>Immutable compilation content; runtime fingerprints and approval grants belong outside this definition.</summary>
internal sealed record PlanDefinition(
    string PlanId,
    PlanProject Project,
    string Cabinet,
    PlanOwner Owner,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int SchemaVersion,
    string CompilerVersion,
    int SnapshotFormatVersion,
    string EplanVersion,
    string EplanVariant,
    ImmutableArray<string> RequiredOperations,
    PlanInputs Inputs,
    long? BindingRevision,
    string? LockDigest,
    string? ContextRef,
    ImmutableArray<PlanResource> Resources,
    ImmutableArray<PlanSource> Sources,
    PlanDigests Digests,
    ImmutableArray<PlanStep> Steps);

internal sealed record PlanProject(string Name, string Path, string? Guid);
internal sealed record PlanOwner(string RunId, string SessionId);
internal sealed record PlanInputs(ImmutableArray<PlanInputStep> Steps);
internal sealed record PlanInputStep(string Op, JsonElement Args);
internal sealed record PlanResource(string Pack, string ExactVersion, string ResourcePath, string ManifestDigest, string FileHash);
internal sealed record PlanSource(
    string Id, string? Project, string? Kind, int? Type, long? Oid, string? Hash,
    string? Pack, string? ExactVersion, string? ResourcePath, string? ManifestDigest, string? FileHash);
internal sealed record PlanDigests(string? Pages, string? Objects, string? Names, string? ChangesDigest);

/// <summary>Args include every default. Operation-specific values and predictions are compiled later.</summary>
internal sealed record PlanStep(
    int Index,
    string Op,
    JsonElement Args,
    ToolTier Tier,
    PlanScope ReadScope,
    PlanScope ModifyScope,
    ImmutableArray<PlanPrecondition> Preconditions,
    ImmutableArray<JsonElement> Expected,
    ImmutableArray<PlanChange> PlannedChanges,
    ImmutableArray<int> DependsOn,
    JsonElement? Prediction,
    bool Effective)
{
    public ImmutableDictionary<string, string> Details { get; init; } = ImmutableDictionary<string, string>.Empty;
    public JsonElement? Coverage { get; init; }
}

/// <summary>All three collections are exact sets; multiple filters intersect (design 4.3).</summary>
internal sealed record PlanScope(ImmutableArray<string> Pages, ImmutableArray<string> Structures, ImmutableArray<PlanLocator> Locators);
internal sealed record PlanLocator(string Project, string Kind, int Type, long Oid, string? Name, string? Page, string? Category, string? Pos);
internal sealed record PlanChange(string Action, string Kind, string Name, string? Property, string? OldValue, string? NewValue);
