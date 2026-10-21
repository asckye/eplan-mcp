using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace EplanMcp.Host.Addin;

/// <summary>install-addin's and uninstall-addin's report as text for the engineer, or as the JSON the Workbench reads (architecture 5.2).</summary>
internal static class AddinOutput
{
    /// <summary>The version of the JSON shape; a change to the shape raises it.</summary>
    public const int SchemaVersion = 1;

    private const string Indent = "    ";

    public static string Text(AddinReport report, CultureInfo culture)
    {
        bool zh = culture.TwoLetterISOLanguageName == "zh";
        var text = new StringBuilder();
        if (report.Dll is { } dll)
        {
            text.Append(AddinTexts.Addin(zh)).AppendLine(dll);
        }

        if (report.Problem is { } problem)
        {
            return text.Append(Tag(AddinOutcome.Failed, zh)).AppendLine(AddinTexts.Problem(problem, report.Action, zh)).ToString();
        }

        if (report.Results.Count == 0)
        {
            return text.AppendLine(AddinTexts.NothingRecorded(zh)).ToString();
        }

        foreach (AddinResult result in report.Results)
        {
            text.Append(Tag(result.Outcome, zh)).Append("EPLAN ").Append(result.Version).Append(result.Variant is { } variant ? " " + variant : string.Empty);
            text.Append(result.ProcessId is { } pid ? AddinTexts.Process(pid, zh) : string.Empty).Append("  ").AppendLine(result.Eplan);
            if (result.Problem is { } issue)
            {
                text.Append(Indent).AppendLine(AddinTexts.Problem(issue, report.Action, zh));
            }

            foreach (AddinNote note in result.Notes)
            {
                text.Append(Indent).AppendLine(AddinTexts.Note(note, zh));
            }
        }

        return text.ToString();
    }

    public static string Json(AddinReport report) => new JsonObject
    {
        ["schemaVersion"] = SchemaVersion,
        ["action"] = report.Action == AddinAction.Install ? "install" : "uninstall",
        ["dll"] = report.Dll,
        ["problem"] = Issue(report.Problem, report.Action),
        ["results"] = new JsonArray([.. report.Results.Select(r => Result(r, report.Action))]),
    }.ToJsonString(HostJson.Options);

    private static JsonObject Result(AddinResult result, AddinAction action) => new()
    {
        ["eplan"] = result.Eplan,
        ["version"] = result.Version,
        ["variant"] = result.Variant,
        ["result"] = result.Outcome switch
        {
            AddinOutcome.Registered => "registered",
            AddinOutcome.Unregistered => "unregistered",
            AddinOutcome.NotConfirmed => "not_confirmed",
            AddinOutcome.Failed => "failed",
            AddinOutcome.Refused => "refused",
            AddinOutcome.Skipped => "skipped",
            _ => throw new UnreachableException($"Outcome {result.Outcome} has no JSON name."),
        },
        ["processId"] = result.ProcessId,
        ["notes"] = new JsonArray([.. result.Notes.Select(n => new JsonObject
        {
            ["code"] = n switch
            {
                AddinNote.VersionUntested => "version_untested",
                AddinNote.UnsupportedVersion => "unsupported_version",
                AddinNote.EplanMissing => "eplan_missing",
                AddinNote.CloseEplan => "close_eplan",
                _ => throw new UnreachableException($"Note {n} has no JSON name."),
            },
            ["zh"] = AddinTexts.Note(n, zh: true),
            ["en"] = AddinTexts.Note(n, zh: false),
        })]),
        ["problem"] = Issue(result.Problem, action),
    };

    private static JsonObject? Issue(AddinIssue? issue, AddinAction action) => issue is null ? null : new JsonObject
    {
        ["code"] = issue.Problem switch
        {
            AddinProblem.AddinNotFound => "addin_not_found",
            AddinProblem.NoEplan => "no_eplan",
            AddinProblem.VariantNotFound => "variant_not_found",
            AddinProblem.UnblockFailed => "unblock_failed",
            AddinProblem.InvalidRecord => "invalid_record",
            AddinProblem.EplanRunning => "eplan_running",
            AddinProblem.OtherUser => "other_user",
            AddinProblem.StartFailed => "start_failed",
            AddinProblem.EplanExited => "eplan_exited",
            AddinProblem.NotLoaded => "not_loaded",
            AddinProblem.PathMismatch => "path_mismatch",
            AddinProblem.HandshakeFailed => "handshake_failed",
            AddinProblem.RecordFailed => "record_failed",
            _ => throw new UnreachableException($"Problem {issue.Problem} has no JSON name."),
        },
        ["zh"] = AddinTexts.Problem(issue, action, zh: true),
        ["en"] = AddinTexts.Problem(issue, action, zh: false),
    };

    private static string Tag(AddinOutcome outcome, bool zh) => "[" + AddinTexts.Outcome(outcome, zh) + "] ";
}
