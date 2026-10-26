using EplanMcp.Spikes.S26;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Spikes;

[Trait("Size", "Small")]
public sealed class SpikeS26R2AssessmentTests
{
    private static readonly string[] _paths = ["target", "other"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteEvidence_IsOnlyReadyForReview_NotVerified(bool export)
    {
        JObject result = Evidence(export);
        JObject assessment = Assess(result, export);
        Assert.Equal("evidence_ready_for_review", (string?)assessment["status"]);
        Assert.False((bool)assessment["unsafeToContinue"]!);
        Assert.Contains("Pending verification", (string)assessment["limitation"]!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rollback", "rollback_unconfirmed", true)]
    [InlineData("snapshot", "incomplete_readback", true)]
    [InlineData("readError", "incomplete_readback", true)]
    [InlineData("unstable", "incomplete_readback", true)]
    [InlineData("manifest", "incomplete_readback", true)]
    [InlineData("link", "export_or_other_project_files_changed", true)]
    [InlineData("settings", "settings_not_restored", true)]
    [InlineData("restoration", "semantic_state_not_restored_or_scope_changed", true)]
    [InlineData("otherState", "semantic_state_not_restored_or_scope_changed", true)]
    [InlineData("otherFiles", "export_or_other_project_files_changed", true)]
    [InlineData("scheme", "last_used_scheme_changed", true)]
    [InlineData("false", "action_or_context_or_messages_failed", false)]
    [InlineData("messages", "action_or_context_or_messages_failed", false)]
    [InlineData("reset", "action_or_context_or_messages_failed", false)]
    [InlineData("context", "action_or_context_or_messages_failed", false)]
    [InlineData("output", "invalid_output", false)]
    [InlineData("noEffect", "needs_effect_sample", false)]
    [InlineData("window", "needs_engineer_window_observation", false)]
    public void FalseSuccessOrMissingEvidence_IsNeverReady(string failure, string status, bool stop)
    {
        JObject result = Evidence(false);
        switch (failure)
        {
            case "rollback":
                result["rollbackReturned"] = false;
                break;
            case "snapshot":
                result["r2Readbacks"]!["after"]!["snapshot"] = new JObject { ["error"] = "disk failure" };
                break;
            case "readError":
                result["r2Readbacks"]!["after"]!["target"]!["readErrors"] = 1;
                break;
            case "unstable":
                result["after"]!["projects"]!["target"]!["files"]!["unstableFiles"] = 1;
                break;
            case "manifest":
                result["r2Readbacks"]!["after"]!["target"]!["manifestHashErrors"] = 1;
                break;
            case "link":
                result["r2Readbacks"]!["after"]!["other"]!["link"]!["sha256"] = "different";
                break;
            case "settings":
                result["r2Readbacks"]!["afterRollback"]!["target"]!["settingsSha256"] = "different";
                break;
            case "restoration":
                result["r2Readbacks"]!["afterRollback"]!["target"]!["semanticSha256"] = "different";
                break;
            case "otherState":
                result["r2Readbacks"]!["after"]!["other"]!["semanticSha256"] = "different";
                break;
            case "otherFiles":
                result["after"]!["projects"]!["other"]!["files"]!["designContentManifestSha256"] = "different";
                break;
            case "scheme":
                result["r2Readbacks"]!["before"]!["lastUsedScheme"] = "original";
                result["r2Readbacks"]!["after"]!["lastUsedScheme"] = "changed";
                break;
            case "false":
                result["returned"] = false;
                break;
            case "messages":
                result["systemMessages"]!["errorCount"] = 1;
                break;
            case "reset":
                result["systemMessages"]!["collectionReset"] = true;
                break;
            case "context":
                result["contextReadBack"]!["PROJECTNAME"]!["exact"] = false;
                break;
            case "output":
                result["r2Output"]!["valid"] = false;
                break;
            case "noEffect":
                result["r2Readbacks"]!["after"]!["target"]!["effectSha256"] = "same";
                break;
            case "window":
                result["windowObservation"]!["mainWindowDisabledObserved"] = true;
                break;
        }
        JObject assessment = Assess(result, false);
        Assert.Equal(status, (string?)assessment["status"]);
        Assert.Equal(stop, (bool)assessment["unsafeToContinue"]!);
    }

    [Fact]
    public void ExportChangingDesignFiles_CannotPassEvenWhenSemanticCountsStayEqual()
    {
        JObject result = Evidence(true);
        result["after"]!["projects"]!["target"]!["files"]!["designContentManifestSha256"] = "different";
        Assert.Equal("export_or_other_project_files_changed", (string?)Assess(result, true)["status"]);
    }

    [Fact]
    public void TypeSwitchMustReachTheRequestedType_AndSkippedSamplesAreNotPasses()
    {
        JObject result = Evidence(false);
        Assert.Equal("unexpected_project_type", (string?)SpikeS26R2Assessment.Assess(result, _paths, "target", false, "Macro")["status"]);
        result["notRun"] = "needs a sample";
        Assert.Equal("not_run", (string?)Assess(result, false)["status"]);
    }

    private static JObject Assess(JObject result, bool export) => SpikeS26R2Assessment.Assess(result, _paths, "target", export, null);

    internal static JObject Evidence(bool export)
    {
        var result = new JObject
        {
            ["returned"] = true,
            ["rollbackReturned"] = true,
            ["closeUndoReturned"] = true,
            ["unsafeToContinue"] = false,
            ["systemMessages"] = new JObject { ["errorCount"] = 0, ["collectionReset"] = false },
            ["contextReadBack"] = new JObject { ["PROJECTNAME"] = new JObject { ["exact"] = true } },
            ["r2Output"] = new JObject { ["valid"] = true },
            ["windowObservation"] = new JObject { ["mainWindowDisabledObserved"] = false },
            ["r2Readbacks"] = new JObject(),
        };
        foreach (string phase in new[] { "before", "after", "afterRollback", "afterSafetyPointDisposed" })
        {
            var state = new JObject { ["snapshot"] = new JObject { ["stamp"] = new JObject { ["changedDuringHash"] = false } } };
            var projects = new JObject();
            foreach (string path in _paths)
            {
                state[path] = new JObject
                {
                    ["semanticSha256"] = !export && phase == "after" && path == "target" ? "changed" : "same",
                    ["effectSha256"] = !export && phase == "after" && path == "target" ? "changed" : "same",
                    ["readErrors"] = 0,
                    ["projectType"] = "Schematic",
                    ["manifestHashErrors"] = 0,
                    ["manifestUnstableFiles"] = 0,
                    ["settingsSha256"] = "same",
                    ["link"] = new JObject { ["sha256"] = "same" }
                };
                projects[path] = new JObject { ["files"] = new JObject { ["hashErrors"] = 0, ["unstableFiles"] = 0, ["designContentManifestSha256"] = "same" } };
            }
            result["r2Readbacks"]![phase] = state;
            result[phase] = new JObject { ["projects"] = projects };
        }
        return result;
    }
}
