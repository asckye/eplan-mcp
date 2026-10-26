using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S26;

// r3 distinguishes refusal from attempted execution without relaxing any post-execution safety decision.
internal static class SpikeS26R3Assessment
{
    internal static JObject Assess(JObject result, string[] paths, string target, bool export, string? expectedType)
    {
        if (result.Value<bool?>("actionAttempted") == false)
        {
            string reason = result.Value<string>("notRun") ?? (string?)result["setupException"]?["message"] ??
                (string?)result["exception"]?["message"] ?? (string?)result["scopeException"]?["message"] ?? "Refused before action.Execute; no action was attempted.";
            result["notRun"] = reason;
            bool stop = result.Value<bool?>("unsafeToContinue") != false || result["scopeException"] is not null || result["closeUndoException"] is not null ||
                result["safetyCheckException"] is not null || result.Value<bool?>("transactionStarted") != false && result.Value<bool?>("rollbackReturned") != true;
            foreach (string path in paths)
            {
                foreach (string field in new[] { "semanticSha256", "settingsSha256", "link.sha256" })
                {
                    JToken? before = result["r2Readbacks"]?["before"]?[path]?.SelectToken(field);
                    JToken? after = result["r2Readbacks"]?["afterSafetyPointDisposed"]?[path]?.SelectToken(field);
                    if (before is not null && after is not null && !JToken.DeepEquals(before, after))
                    { stop = true; }
                }
                foreach (string field in new[] { "counts", "files.designContentManifestSha256" })
                {
                    JToken? before = result["before"]?["projects"]?[path]?.SelectToken(field);
                    JToken? after = result["afterSafetyPointDisposed"]?["projects"]?[path]?.SelectToken(field);
                    if (before is not null && after is not null && !JToken.DeepEquals(before, after))
                    { stop = true; }
                }
            }
            return new JObject
            {
                ["status"] = "not_run",
                ["reason"] = reason,
                ["unsafeToContinue"] = stop,
                ["limitation"] = "No action was attempted. Missing baseline fields are not inferred; existing window/project/scope safety failures still stop the mode."
            };
        }
        // A stale notRun field must never hide an actual native call or missing execution evidence.
        var attempted = (JObject)result.DeepClone();
        attempted.Remove("notRun");
        JObject assessment = SpikeS26R2Assessment.Assess(attempted, paths, target, export, expectedType);
        if (assessment.Value<string>("status") == "evidence_ready_for_review" &&
            result["r2Readbacks"]?.Children<JProperty>().Any(phase => paths.Any(path => phase.Value[path]?["knownGaps"] is JArray gaps && gaps.Count > 0)) == true)
        { assessment["status"] = "needs_gap_review"; }
        return assessment;
    }
}
