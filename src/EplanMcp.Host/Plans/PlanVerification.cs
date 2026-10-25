using System.Collections.Immutable;
using System.Text.Json;

namespace EplanMcp.Host.Plans;

/// <summary>WholeProject also covers complete multi-chunk reads whose SnapshotHash is null.</summary>
internal sealed record PlanEvidence(PlanSnapshot Snapshot, bool WholeProject = false)
{
    // Transient approved absence, reconstructed from the plan and page-name evidence; no persisted member is added.
    internal ImmutableArray<string> AbsentPages { get; init; } = [];

    public bool Covers(string? page) => WholeProject || Snapshot.SnapshotHash is not null
        || Snapshot.Pages.Any(p => p.Name == page) || page is not null && AbsentPages.Contains(page, StringComparer.Ordinal);
}

/// <summary>NoWriteSincePrevious must be established by the caller, never inferred from an execution outcome.</summary>
internal sealed record PlanStepEvidence(int Index, string Execution, PlanEvidence? Before, PlanEvidence? After,
    bool NoWriteSincePrevious = false);
internal sealed record PlanValidation(int Step, JsonElement Locator, string Check, string Status, string Reason);
internal sealed record PlanDiffEntry(int Step, string Class, JsonElement Locator, string Property,
    string Status, string Reason, string Attribution, string? Before, string? After, string? Expected = null);
internal sealed record PlanVerificationCoverage(int Step, string Window, ImmutableArray<string> UnobservedPages,
    int SkippedConnections, ImmutableArray<string> PropertyGaps);

internal sealed record PlanVerification(string Verification, ImmutableArray<PlanValidation> Validation,
    ImmutableArray<PlanDiffEntry> Diff, ImmutableArray<PlanVerificationCoverage> Coverage)
{
    public bool OutsideScope => Diff.Any(d => d.Class == "outside_scope");

    public PlanResult ApplyTo(PlanResult result) => result with
    {
        Verification = Verification,
        Validation = JsonSerializer.SerializeToElement(new { electrical = "skipped", drawing = "skipped", entries = Validation }, PlanJson.Options),
        Diff = JsonSerializer.SerializeToElement(Diff, PlanJson.Options),
        Coverage = JsonSerializer.SerializeToElement(Coverage, PlanJson.Options),
    };
}
