using EplanMcp.Spikes.S26;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Spikes;

[Trait("Size", "Small")]
public sealed class SpikeS26R3AssessmentTests
{
    private static readonly string[] _paths = ["target", "other"];

    [Fact]
    public void BaselineGroupReadFailsBeforeTransaction_IsNotRun_AndDoesNotStopNextCandidate()
    {
        JObject result = Refused();
        foreach (string phase in new[] { "before", "afterSafetyPointDisposed" })
        {
            foreach (string path in _paths)
            {
                result["r2Readbacks"]![phase]![path]!["readErrors"] = 1;
                result["r2Readbacks"]![phase]![path]!["readFailures"] = JArray.Parse("""[{"field":"groups","exception":{"type":"System.InvalidCastException","message":"A base wrapper cannot be cast to Group."}}]""");
            }
        }
        JObject assessment = Assess(result);
        Assert.Equal("not_run", (string?)assessment["status"]);
        Assert.Equal("Baseline read failed: target; groups", (string?)assessment["reason"]);
        Assert.Equal((string?)assessment["reason"], (string?)result["notRun"]);
        Assert.False((bool)assessment["unsafeToContinue"]!);
        Assert.Equal("evidence_ready_for_review", (string?)Assess(Attempted())["status"]);
    }

    [Theory]
    [InlineData("baseSafety")]
    [InlineData("missingSafety")]
    [InlineData("scope")]
    [InlineData("undo")]
    [InlineData("safetyRead")]
    [InlineData("transactionRollback")]
    [InlineData("state")]
    [InlineData("file")]
    [InlineData("counts")]
    public void PreActionRefusal_KeepsEveryObservedSafetyFailure(string failure)
    {
        JObject result = Refused();
        switch (failure)
        {
            case "baseSafety":
                result["unsafeToContinue"] = true;
                break;
            case "missingSafety":
                result.Remove("unsafeToContinue");
                break;
            case "scope":
                result["scopeException"] = new JObject { ["message"] = "SafetyPoint failed" };
                break;
            case "undo":
                result["closeUndoException"] = new JObject { ["message"] = "CloseUndo failed" };
                break;
            case "safetyRead":
                result["safetyCheckException"] = new JObject { ["message"] = "Open project list unavailable" };
                break;
            case "transactionRollback":
                result["transactionStarted"] = true;
                break;
            case "state":
                result["r2Readbacks"]!["afterSafetyPointDisposed"]!["other"]!["semanticSha256"] = "changed";
                break;
            case "file":
                result["afterSafetyPointDisposed"]!["projects"]!["target"]!["files"]!["designContentManifestSha256"] = "changed";
                break;
            case "counts":
                result["afterSafetyPointDisposed"]!["projects"]!["target"]!["counts"] = new JObject { ["pages"] = 2 };
                break;
        }
        JObject assessment = Assess(result);
        Assert.Equal("not_run", (string?)assessment["status"]);
        Assert.True((bool)assessment["unsafeToContinue"]!);
    }

    [Fact]
    public void ContextRefusalInsideTransaction_CanContinueOnlyAfterConfirmedCleanup()
    {
        JObject result = Refused();
        result["transactionStarted"] = true;
        result["rollbackReturned"] = true;
        result["closeUndoReturned"] = true;
        Assert.False((bool)Assess(result)["unsafeToContinue"]!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void NativeCallOrUnknownAttempt_MustStillRequireRollback(bool? attempted)
    {
        JObject result = Refused();
        result["actionAttempted"] = attempted;
        result["notRun"] = "Stale label must not hide attempted execution";
        JObject assessment = Assess(result);
        Assert.Equal("rollback_unconfirmed", (string?)assessment["status"]);
        Assert.True((bool)assessment["unsafeToContinue"]!);
    }

    [Fact]
    public void KnownUnresolvedGroupHandle_IsRecordedForReview_NotSilentlyPassed()
    {
        JObject result = Attempted();
        result["r2Readbacks"]!["before"]!["target"]!["knownGaps"] = JArray.Parse("""[{"field":"groups[0]","reason":"Unresolved group wrapper; handle retained"}]""");
        JObject assessment = Assess(result);
        Assert.Equal("needs_gap_review", (string?)assessment["status"]);
        Assert.False((bool)assessment["unsafeToContinue"]!);
        result["r2Readbacks"]!["afterRollback"]!["target"]!["semanticSha256"] = "different";
        Assert.True((bool)Assess(result)["unsafeToContinue"]!);
    }

    private static JObject Assess(JObject result) => SpikeS26R3Assessment.Assess(result, _paths, "target", false, null);

    private static JObject Attempted()
    {
        JObject result = SpikeS26R2AssessmentTests.Evidence(false);
        result["actionAttempted"] = true;
        result["transactionStarted"] = true;
        return result;
    }

    private static JObject Refused()
    {
        JObject result = Attempted();
        result["actionAttempted"] = false;
        result["transactionStarted"] = false;
        result["returned"] = null;
        result["setupException"] = new JObject { ["message"] = "Baseline read failed: target; groups" };
        foreach (string field in new[] { "after", "afterRollback", "rollbackReturned", "closeUndoReturned" })
        { result.Remove(field); }
        foreach (string phase in new[] { "after", "afterRollback" })
        { ((JObject)result["r2Readbacks"]!).Remove(phase); }
        foreach (string path in _paths)
        { result["before"]!["projects"]![path]!["counts"] = new JObject { ["pages"] = 1 }; result["afterSafetyPointDisposed"]!["projects"]![path]!["counts"] = new JObject { ["pages"] = 1 }; }
        return result;
    }
}
