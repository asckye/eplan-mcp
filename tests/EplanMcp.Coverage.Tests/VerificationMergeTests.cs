using EplanMcp.Coverage.Mapping;

namespace EplanMcp.Coverage.Tests;

/// <summary>Pure in-memory merging; filesystem and generated-report tests are Medium.</summary>
[Trait("Size", "Small")]
public sealed class VerificationMergeTests
{
    private const string Version = "2026.0.3.25421", Id = "O:Eplan.EplApi.DataModel.Page.Name";
    private const string Manual = "# reviewed\nlive:\n  reviewed_tool: [{date: 2026-10-31, result: pass, basis: stated, source: api-notes A11}]\napi: []\navailability: []\n";
    private static readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal) { [Id] = "api-read" };
    private static VerificationRun Run(string id = Id) => new()
    {
        Date = "2026-10-31",
        Eplan = Version,
        Entries = [new() { Id = id, Kind = "api-read", Outcome = "ok", Seconds = 0.5 }],
    };

    [Fact]
    public void MergeIsIdempotent()
    {
        string once = VerificationImport.MergeText(Manual, Run(), _ids, Version);
        Assert.Equal(once, VerificationImport.MergeText(once, Run(), _ids, Version));
        Assert.Contains("\"date\":\"2026-10-31\"", once, StringComparison.Ordinal);
        Assert.Contains("\"eplan\":\"" + Version + "\"", once, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownIdIsReportedAndDroppedWhileKnownEvidenceIsKept()
    {
        var unknown = new List<string>();
        string merged = VerificationImport.MergeText(Manual, new[] { Run(), Run("O:unknown") }, _ids, Version, unknown.Add);
        Assert.Equal(["O:unknown"], unknown);
        Assert.StartsWith(Manual, merged, StringComparison.Ordinal);
        Assert.Contains(Id, merged, StringComparison.Ordinal);
        Assert.DoesNotContain("O:unknown", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewedManualEntriesAndCommentsRemainVerbatim()
    {
        string once = VerificationImport.MergeText(Manual, Run(), _ids, Version);
        Assert.StartsWith(Manual, once, StringComparison.Ordinal);
        string withTail = once + "# manually reviewed tail\ntools: {}\n";
        Assert.Equal(withTail, VerificationImport.MergeText(withTail, Run(), _ids, Version));
    }

    [Theory]
    [InlineData("modal")]
    [InlineData("outcome_unknown")]
    [InlineData("failed:needs_context")]
    public void LaterFilesReplaceTheSameIdAndPreserveOtherResults(string outcome)
    {
        VerificationRun later = Run();
        later.Date = "2026-11-01";
        later.Entries[0].Outcome = outcome;
        var ids = new Dictionary<string, string>(_ids, StringComparer.Ordinal) { ["O:other"] = "api-read" };
        string merged = VerificationImport.MergeText(Manual, new[] { Run(), Run("O:other"), later }, ids, Version);
        Assert.StartsWith(Manual, merged, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"" + outcome + "\"", merged, StringComparison.Ordinal);
        Assert.Contains("\"date\":\"2026-11-01\"", merged, StringComparison.Ordinal);
        Assert.Contains("\"O:other\"", merged, StringComparison.Ordinal);
        Assert.Equal(merged, VerificationImport.MergeText(merged, new[] { later }, ids, Version));
    }
}
