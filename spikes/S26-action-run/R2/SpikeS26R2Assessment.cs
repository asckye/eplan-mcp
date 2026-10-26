using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S26;

// Pure evidence decisions: linked into offline tests; never infers verification from action.Execute == true.
internal static class SpikeS26R2Assessment
{
    internal static JObject Assess(JObject result, string[] paths, string target, bool export, string? expectedType)
    {
        JObject Done(string status, bool stop = false) => new()
        {
            ["status"] = status,
            ["unsafeToContinue"] = stop,
            ["limitation"] = "Pending verification. Engineer must confirm effects, dialogs, settings and final tier. No Commit or Ctrl+Z test; sampled properties do not cover all project data."
        };
        if (result["notRun"] is not null)
        { return Done("not_run"); }
        if (result.Value<bool?>("unsafeToContinue") == true)
        { return Done("unsafe_cleanup", true); }
        if (result.Value<bool?>("rollbackReturned") != true || result["scopeException"] is not null || result["closeUndoException"] is not null)
        { return Done("rollback_unconfirmed", true); }
        string[] phases = ["before", "after", "afterRollback", "afterSafetyPointDisposed"];
        foreach (string phase in phases)
        {
            if (result["r2Readbacks"]?[phase]?["snapshot"]?["stamp"]?.Value<bool?>("changedDuringHash") != false || paths.Any(path =>
                result["r2Readbacks"]?[phase]?[path]?.Value<int?>("readErrors") != 0 ||
                result["r2Readbacks"]?[phase]?[path]?.Value<int?>("manifestHashErrors") != 0 ||
                result["r2Readbacks"]?[phase]?[path]?.Value<int?>("manifestUnstableFiles") != 0 ||
                result[phase]?["projects"]?[path]?["files"]?.Value<int?>("hashErrors") != 0 ||
                result[phase]?["projects"]?[path]?["files"]?.Value<int?>("unstableFiles") != 0))
            { return Done("incomplete_readback", true); }
        }
        foreach (string path in paths)
        {
            JToken? before = result["r2Readbacks"]?["before"]?[path]?["semanticSha256"];
            foreach (string phase in phases.Skip(1))
            {
                JToken? after = result["r2Readbacks"]?[phase]?[path]?["semanticSha256"];
                if (before is null || after is null)
                { return Done("incomplete_readback", true); }
                if ((phase != "after" || export || !string.Equals(path, target, StringComparison.OrdinalIgnoreCase)) && !JToken.DeepEquals(before, after))
                { return Done("semantic_state_not_restored_or_scope_changed", true); }
                JToken? beforeSettings = result["r2Readbacks"]?["before"]?[path]?["settingsSha256"];
                JToken? afterSettings = result["r2Readbacks"]?[phase]?[path]?["settingsSha256"];
                if ((phase != "after" || export) && (beforeSettings is null || afterSettings is null || !JToken.DeepEquals(beforeSettings, afterSettings)))
                { return Done("settings_not_restored", true); }
                JToken? beforeFiles = result["before"]?["projects"]?[path]?["files"]?["designContentManifestSha256"];
                JToken? afterFiles = result[phase]?["projects"]?[path]?["files"]?["designContentManifestSha256"];
                JToken? beforeLink = result["r2Readbacks"]?["before"]?[path]?["link"]?["sha256"];
                JToken? afterLink = result["r2Readbacks"]?[phase]?[path]?["link"]?["sha256"];
                if ((export || !string.Equals(path, target, StringComparison.OrdinalIgnoreCase)) &&
                    (beforeFiles is null || afterFiles is null || beforeLink is null || afterLink is null || !JToken.DeepEquals(beforeFiles, afterFiles) || !JToken.DeepEquals(beforeLink, afterLink)))
                { return Done("export_or_other_project_files_changed", true); }
            }
        }
        if (result["r2Readbacks"]?["before"]?["lastUsedScheme"] is JToken last && phases.Skip(1).Any(phase =>
            !JToken.DeepEquals(last, result["r2Readbacks"]?[phase]?["lastUsedScheme"])))
        { return Done("last_used_scheme_changed", true); }
        if (result.Value<bool?>("returned") != true || result["exception"] is not null || result["setupException"] is not null ||
            result.Value<bool?>("closeUndoReturned") != true || result["systemMessages"]?.Value<int?>("errorCount") != 0 ||
            result["systemMessages"]?.Value<bool?>("collectionReset") != false ||
            result["contextReadBack"] is not JObject echo || echo.Count == 0 || echo.Properties().Any(p => p.Value.Value<bool?>("exact") != true))
        { return Done("action_or_context_or_messages_failed"); }
        if (result["r2Output"]?.Value<bool?>("valid") != true)
        { return Done("invalid_output"); }
        if (!export)
        {
            JToken? before = result["r2Readbacks"]?["before"]?[target]?["effectSha256"];
            JToken? after = result["r2Readbacks"]?["after"]?[target]?["effectSha256"];
            if (before is null || after is null || JToken.DeepEquals(before, after))
            { return Done("needs_effect_sample"); }
            if (expectedType is not null && result["r2Readbacks"]?["after"]?[target]?.Value<string>("projectType") != expectedType)
            { return Done("unexpected_project_type"); }
        }
        if (result["windowObservation"]?.Value<bool?>("mainWindowDisabledObserved") != false)
        { return Done("needs_engineer_window_observation"); }
        return Done("evidence_ready_for_review");
    }
}
