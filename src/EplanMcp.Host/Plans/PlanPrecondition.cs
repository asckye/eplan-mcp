using System.Text.Json.Serialization;

namespace EplanMcp.Host.Plans;

/// <summary>Design 4.3's closed union of persistent checks; runtime identifiers are never preconditions.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tag")]
[JsonDerivedType(typeof(PlanObjectHash), "objectHash")]
[JsonDerivedType(typeof(PlanPageHash), "pageHash")]
[JsonDerivedType(typeof(PlanMembers), "members")]
[JsonDerivedType(typeof(PlanAbsent), "absent")]
[JsonDerivedType(typeof(PlanResourceHash), "resource")]
[JsonDerivedType(typeof(PlanBinding), "binding")]
internal abstract record PlanPrecondition;

internal sealed record PlanObjectHash(PlanLocator Locator, string Hash) : PlanPrecondition;
internal sealed record PlanPageHash(PlanLocator Locator, string Hash) : PlanPrecondition;
internal sealed record PlanMembers(PlanScope Scope, string Hash, int Count) : PlanPrecondition;
internal sealed record PlanAbsent(string Kind, string Name) : PlanPrecondition;
internal sealed record PlanResourceHash(string Sha256) : PlanPrecondition;
internal sealed record PlanBinding(long Revision, string LockDigest) : PlanPrecondition;
