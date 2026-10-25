using System.Collections.Immutable;
using System.Text.Json;

namespace EplanMcp.Host.Plans;

internal static class PlanCoverage
{
    internal static PlanVerificationCoverage Summary(PlanVerificationCoverage coverage)
    {
        var pages = new List<string>();
        int remaining = 512;
        foreach (string page in coverage.UnobservedPages.Take(8))
        {
            remaining -= JsonSerializer.SerializeToUtf8Bytes(page).Length;
            if (remaining < 0)
            {
                break;
            }
            pages.Add(page);
        }
        return coverage with
        {
            UnobservedPages = [.. pages],
            PropertyGaps = [.. coverage.PropertyGaps, "coverage.fullListInEvidence"],
        };
    }

    internal static async Task<PlanVerification> StoreAsync(PlanStore store, string planId, PlanVerification verification,
        IDictionary<string, string> references, CancellationToken cancellationToken)
    {
        var summaries = new List<PlanVerificationCoverage>();
        var keys = verification.Coverage.Select(c => PlanLocalEvidenceReader.ReferenceKey(c.Step, c.Window) + ":coverage").ToHashSet(StringComparer.Ordinal);
        foreach (string key in references.Keys.Where(k => k.EndsWith(":coverage", StringComparison.Ordinal) && !keys.Contains(k)).ToArray())
        {
            references.Remove(key);
        }
        foreach (PlanVerificationCoverage coverage in verification.Coverage)
        {
            string key = PlanLocalEvidenceReader.ReferenceKey(coverage.Step, coverage.Window) + ":coverage";
            references[key] = await store.PutEvidenceAsync(planId, JsonSerializer.SerializeToUtf8Bytes(coverage, PlanJson.Options), cancellationToken).ConfigureAwait(false);
            summaries.Add(Summary(coverage));
        }
        return verification with { Coverage = [.. summaries] };
    }

    internal static async Task<ImmutableArray<PlanVerificationCoverage>> ReadAsync(PlanStore store, PlanRecord record, CancellationToken cancellationToken)
    {
        var result = new List<PlanVerificationCoverage>();
        if (record.State.Result?.Coverage is { } coverage && coverage.TryGetProperty("evidence", out JsonElement references))
        {
            foreach (JsonProperty reference in references.EnumerateObject().Where(p => p.Name.EndsWith(":coverage", StringComparison.Ordinal)))
            {
                byte[] bytes = await store.ReadEvidenceAsync(record.Definition.PlanId, reference.Value.GetString()!, cancellationToken).ConfigureAwait(false);
                result.Add(JsonSerializer.Deserialize<PlanVerificationCoverage>(bytes, PlanJson.Options)!);
            }
        }
        return [.. result];
    }
}
