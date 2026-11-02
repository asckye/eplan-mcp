using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;
using EplanMcp.Coverage.Tests.Reporting;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Medium")]
public sealed class VerificationImportTests
{
    private const string Version = "2026.0.3.25421";
    private const string Id = "O:Eplan.EplApi.Test.Page.Remove";
    private static readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal) { [Id] = "api-read", [Id + "#write"] = "api-write", ["A:export/DXF"] = "action" };
    private static VerificationRun Run(params VerificationRecord[] records) => new() { Date = "2026-10-31", Eplan = Version, Entries = [.. records] };
    private static VerificationRecord Read(string id = Id) => new() { Id = id, Kind = "api-read", Outcome = "ok" };

    [Fact]
    public void MergeIsIdempotentAndPreservesReviewedManualTextAndOtherVerificationEntries()
    {
        using TempRepository repository = ReportFixture.Create();
        string original = repository.Read(Overlay.RelativePath);
        string path = Path.Combine(repository.Root, Overlay.RelativePath);
        VerificationImport.Merge(path, Run(Read()), _ids, Version);
        string once = repository.Read(Overlay.RelativePath);
        VerificationImport.Merge(path, Run(Read()), _ids, Version);
        Assert.Equal(once, repository.Read(Overlay.RelativePath));
        Assert.StartsWith(original, once, StringComparison.Ordinal);
        VerificationImport.Merge(path, Run(new VerificationRecord { Id = "A:export/DXF", Kind = "action", Outcome = "failed" }), _ids, Version);
        Overlay overlay = Overlay.Load(repository.Root, [.. HostTools.Read(repository.Root).Tools.Select(t => t.Name)]);
        Assert.Equal(2, overlay.Verification.Count);
        Assert.Equal("ok", overlay.Verification[Id].Outcome);
        Assert.Equal("2026-10-31", overlay.Verification[Id].Date);
        Assert.Equal(Version, overlay.Verification[Id].Eplan);
        Assert.Equal(Id, overlay.Verification[Id].Id);
        Assert.Single(Assert.Single(overlay.Live).Value);
        Assert.Single(overlay.Api);
        Assert.Single(overlay.Availability);
        Assert.StartsWith(original, repository.Read(Overlay.RelativePath), StringComparison.Ordinal);
    }

    [Fact]
    public void MergeUpdatesOnlyTheImportedIdEvenWhenVerificationIsNotTheLastSection()
    {
        using TempRepository repository = new();
        repository.Write(Overlay.RelativePath, "verification:\n  \"" + Id + "\": {kind: api-read, outcome: failed, date: 2026-10-30, eplan: " + Version + "}\n\n# reviewed by owner\napi: []\n");
        string path = Path.Combine(repository.Root, Overlay.RelativePath);
        VerificationImport.Merge(path, Run(Read()), _ids, Version);
        Assert.Contains("api: []", repository.Read(Overlay.RelativePath), StringComparison.Ordinal);
        Assert.Contains("# reviewed by owner", repository.Read(Overlay.RelativePath), StringComparison.Ordinal);
        Assert.Equal("ok", Overlay.Load(repository.Root, []).Verification[Id].Outcome);
    }

    [Fact]
    public void DropsUnknownIdWithoutChangingReviewedOverlayEntries()
    {
        using TempRepository repository = ReportFixture.Create();
        string original = repository.Read(Overlay.RelativePath);
        VerificationImport.Merge(Path.Combine(repository.Root, Overlay.RelativePath), Run(Read(), Read("O:Unknown")), _ids, Version);
        string merged = repository.Read(Overlay.RelativePath);
        Assert.StartsWith(original, merged, StringComparison.Ordinal);
        Assert.DoesNotContain("O:Unknown", merged, StringComparison.Ordinal);
        Assert.Single(Overlay.Load(repository.Root, [.. HostTools.Read(repository.Root).Tools.Select(t => t.Name)]).Verification);
    }

    [Theory]
    [InlineData("not-a-date", Version, false)]
    [InlineData("2026-10-31", "2027.0", false)]
    [InlineData("2026-10-31", Version, true)]
    public void RejectsBadProvenanceAndDuplicateIds(string date, string version, bool duplicate)
    {
        using TempRepository repository = ReportFixture.Create();
        VerificationRun run = duplicate ? Run(Read(), Read()) : Run(Read());
        run.Date = date;
        run.Eplan = version;
        Assert.Throws<InvalidDataException>(() => VerificationImport.Merge(Path.Combine(repository.Root, Overlay.RelativePath), run, _ids, Version));
    }

    [Theory]
    [InlineData("action", "ok", 0)]
    [InlineData("api-read", "success", 0)]
    [InlineData("api-read", "skipped:", 0)]
    [InlineData("api-read", "ok", -1)]
    public void RejectsInvalidEvidence(string kind, string outcome, double seconds)
    {
        using TempRepository repository = ReportFixture.Create();
        var evidence = new VerificationRecord { Id = Id, Kind = kind, Outcome = outcome, Seconds = seconds };
        Assert.Throws<InvalidDataException>(() => VerificationImport.Merge(Path.Combine(repository.Root, Overlay.RelativePath), Run(evidence), _ids, Version));
    }

    [Theory]
    [InlineData("api-read", "ok", false, null, true)]
    [InlineData("action", "ok", true, true, true)]
    [InlineData("api-write", "ok", true, true, true)]
    [InlineData("api-write", "ok", false, null, false)]
    [InlineData("action", "ok", true, false, false)]
    [InlineData("api-read", "ok", true, null, false)]
    [InlineData("api-read", "ok", true, true, false)]
    [InlineData("api-write", "rolled_back", true, true, false)]
    [InlineData("action", "outcome_unknown", false, null, false)]
    [InlineData("action", "skipped:no_object", false, null, false)]
    public void PromotionRequiresSuccessAndReadOnlyOrVerifiedUndo(string kind, string outcome, bool changed, bool? restores, bool expected)
    {
        Assert.Equal(expected, new VerificationRecord { Kind = kind, Outcome = outcome, Changed = changed, UndoRestores = restores }.Verifies);
    }

    [Fact]
    public void GenerateMarksIndependentlyVerifiedApiGroupsAndSelectsOnlyEligiblePromotionData()
    {
        using TempRepository repository = ReportFixture.Create();
        VerificationImport.Merge(Path.Combine(repository.Root, Overlay.RelativePath), Run(Read(),
            new() { Id = "A:export/DXF", Kind = "action", Outcome = "outcome_unknown" }), _ids, Version);
        CoverageReport report = CoverageReport.Build(repository.Root);
        Assert.Equal("live_verified", report.Units.Single(u => u.Unit == Id).Status);
        Assert.Equal(2, report.Layers["operation"].LiveVerified);
        Assert.Equal(1, report.Layers["operation"].EnvBlocked);
        string whitelist = VerificationImport.Whitelist(repository.Root);
        Assert.Contains(Id, whitelist, StringComparison.Ordinal);
        Assert.DoesNotContain("A:export/DXF", whitelist, StringComparison.Ordinal);
        string markdown = CoverageMarkdown.Render(repository.Root);
        Assert.Contains("| api-read | 0 | 1 | 0 | 0 | 0 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| action | 0 | 0 | 0 | 1 | 0 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void LatestFailedRunReplacesPriorSuccess()
    {
        using TempRepository repository = ReportFixture.Create();
        string path = Path.Combine(repository.Root, Overlay.RelativePath);
        VerificationImport.Merge(path, Run(Read()), _ids, Version);
        VerificationRecord failure = Read();
        failure.Outcome = "failed";
        VerificationImport.Merge(path, Run(failure), _ids, Version);
        Assert.DoesNotContain(Id, VerificationImport.Whitelist(repository.Root), StringComparison.Ordinal);
        Assert.Equal("implemented", CoverageReport.Build(repository.Root).Units.Single(u => u.Unit == Id).Status);
    }

    [Fact]
    public void MultipleFilesAreValidatedBeforeWritingAndLaterEvidenceWins()
    {
        using TempRepository repository = ReportFixture.Create();
        string path = Path.Combine(repository.Root, Overlay.RelativePath);
        string original = File.ReadAllText(path);
        VerificationRun bad = Run(new VerificationRecord { Id = Id, Kind = "action", Outcome = "ok" });
        Assert.Throws<InvalidDataException>(() => VerificationImport.Merge(path, new[] { Run(Read()), bad }, _ids, Version));
        Assert.Equal(original, File.ReadAllText(path));
        VerificationRecord later = Read();
        later.Outcome = "modal";
        VerificationImport.Merge(path, new[] { Run(Read()), Run(new VerificationRecord { Id = "A:export/DXF", Kind = "action", Outcome = "failed:needs_context" }),
            Run(later) }, _ids, Version);
        Assert.Equal("modal", Overlay.Load(repository.Root, [.. HostTools.Read(repository.Root).Tools.Select(t => t.Name)]).Verification[Id].Outcome);
        Assert.Contains("| action | 0 | 0 | 0 | 0 | 0 | 1 |", CoverageMarkdown.Render(repository.Root), StringComparison.Ordinal);
    }

    [Fact]
    public void DenominatorAndEveryExclusionRemainUnchangedWhenGroupsAndActionsAreVerified()
    {
        using TempRepository repository = ReportFixture.Create();
        CoverageReport before = CoverageReport.Build(repository.Root);
        Dictionary<string, string> ids = VerificationImport.EligibleIds(repository.Root);
        Assert.DoesNotContain("A:print", ids.Keys);
        Assert.DoesNotContain("O:unknown", ids.Keys);
        Assert.DoesNotContain("D:Page", ids.Keys);
        VerificationImport.Merge(Path.Combine(repository.Root, Overlay.RelativePath), Run(Read(),
            new() { Id = "A:export/DXF", Kind = "action", Outcome = "ok" },
            new() { Id = "A:print", Kind = "action", Outcome = "ok" }, Read("O:unknown")), ids, Version);
        CoverageReport after = CoverageReport.Build(repository.Root);
        foreach (string layer in before.Layers.Keys)
        {
            Assert.Equal(before.Layers[layer].Denominator, after.Layers[layer].Denominator);
            Assert.Equal(before.Layers[layer].Excluded, after.Layers[layer].Excluded);
        }
        Assert.Equal("live_verified", after.Units.Single(u => u.Unit == "A:export/DXF").Status);
        Assert.Equal(2, after.Verification.Count);
        Assert.DoesNotContain("A:print", VerificationImport.Whitelist(repository.Root), StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingEvidenceForAnOverlayExcludedGroupIsReportedAndRemoved()
    {
        using TempRepository repository = ReportFixture.Create();
        string path = Path.Combine(repository.Root, Overlay.RelativePath);
        VerificationImport.Merge(path, Run(Read()), _ids, Version);
        string original = repository.Read(Overlay.RelativePath).Replace("api:\n", "api:\n  - {match: \"" + Id + "\", status: excluded, reason: not_applicable, note: reviewed, source: owner}\n", StringComparison.Ordinal);
        repository.Write(Overlay.RelativePath, original);
        var unknown = new List<string>();
        string merged = VerificationImport.MergeText(original, Run(Read()), VerificationImport.EligibleIds(repository.Root), Version, unknown.Add);
        Assert.Equal([Id], unknown);
        Assert.Equal(original[..original.IndexOf("verification:", StringComparison.Ordinal)], merged[..merged.IndexOf("verification:", StringComparison.Ordinal)]);
        Assert.Empty(Overlay.Parse(merged, [.. HostTools.Read(repository.Root).Tools.Select(t => t.Name)]).Verification);
    }
}
