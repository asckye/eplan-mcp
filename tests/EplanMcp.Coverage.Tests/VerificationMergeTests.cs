using EplanMcp.Coverage.Mapping;

namespace EplanMcp.Coverage.Tests;

/// <summary>Pure in-memory merging; filesystem and generated-report tests are Medium.</summary>
[Trait("Size", "Small")]
public sealed class VerificationMergeTests
{
    [Theory]
    [InlineData("PROJECT_NOT_OPEN: project is closed")]
    [InlineData("SESSION_NOT_SELECTED: two EPLAN instances")]
    public void LostProjectOrSessionIsNotAnObservedFailureAndDoesNotReplaceEarlierEvidence(string detail)
    {
        string original = VerificationImport.MergeText(Manual, Run(), _ids, Version);
        VerificationRun run = Run();
        run.Entries[0].Outcome = "failed";
        run.Entries[0].Detail = detail;
        var notObserved = new List<string>();
        Assert.Equal(original, VerificationImport.MergeText(original, run, _ids, Version, reportNotObserved: notObserved.Add));
        Assert.Equal([Id], notObserved);
        string merged = VerificationImport.MergeText(Manual, run, _ids, Version);
        Assert.Empty(Overlay.Parse(merged, ["reviewed_tool"]).Verification);
    }

    [Fact]
    public void JsonStopAndDetailFieldsReachTheImportFilter()
    {
        const string json = """
            {"date":"2026-11-01","eplan":"2026.0.3.25421","stopped_at":"O:crash","stop_reason":"eplan_lost","entries":[
              {"id":"O:closed","kind":"api-read","outcome":"failed","detail":"PROJECT_NOT_OPEN","seconds":25.9},
              {"id":"O:crash","kind":"api-write","outcome":"outcome_unknown","detail":"EPLAN process exited during the trial"},
              {"id":"O:later","kind":"api-read","outcome":"failed"}]}
            """;
        VerificationRun run = System.Text.Json.JsonSerializer.Deserialize<VerificationRun>(json, CoverageJson.Options)!;
        var ids = new Dictionary<string, string>(StringComparer.Ordinal) { ["O:closed"] = "api-read", ["O:crash"] = "api-write", ["O:later"] = "api-read" };
        var notObserved = new List<string>();
        Overlay overlay = Overlay.Parse(VerificationImport.MergeText(Manual, run, ids, Version, reportNotObserved: notObserved.Add), ["reviewed_tool"]);
        Assert.Equal(["O:closed", "O:later"], notObserved);
        Assert.Equal("O:crash", Assert.Single(overlay.Verification).Key);
    }

    [Fact]
    public void ImportRetainsTheUnknownCrashAndIgnoresAll708EntriesAfterAnEplanLostStop()
    {
        const string crash = "O:Eplan.EplApi.DataModel.Page.ConvertToGraphics#write";
        var ids = new Dictionary<string, string>(_ids, StringComparer.Ordinal) { [crash] = "api-write" };
        VerificationRun run = Run();
        run.StopReason = "eplan_lost";
        run.StoppedAt = crash;
        run.Entries.Add(new() { Id = crash, Kind = "api-write", Outcome = "outcome_unknown", Seconds = 25.9, Detail = "EPLAN process exited during the trial" });
        for (int i = 0; i < 708; i++)
        {
            string id = "O:untried." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ids[id] = "api-read";
            run.Entries.Add(new() { Id = id, Kind = "api-read", Outcome = "failed", Seconds = -1 });
        }
        var notObserved = new List<string>();
        string merged = VerificationImport.MergeText(Manual, run, ids, Version, reportNotObserved: notObserved.Add);
        Overlay overlay = Overlay.Parse(merged, ["reviewed_tool"]);
        Assert.Equal(708, notObserved.Count);
        Assert.Equal(2, overlay.Verification.Count);
        Assert.Equal("outcome_unknown", overlay.Verification[crash].Outcome);
        Assert.Equal(25.9, overlay.Verification[crash].Seconds);
        Assert.Equal("ok", overlay.Verification[Id].Outcome);
    }

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
