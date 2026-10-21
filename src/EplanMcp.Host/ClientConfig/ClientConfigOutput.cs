using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace EplanMcp.Host.ClientConfig;

/// <summary>client-config's report as text for the engineer, or as the JSON the Workbench reads (architecture 5.2).</summary>
internal static class ClientConfigOutput
{
    /// <summary>The version of the JSON shape; a change to the shape raises it.</summary>
    public const int SchemaVersion = 1;

    private const string Indent = "    ";

    public static string Text(ClientConfigReport report, CultureInfo culture)
    {
        bool zh = culture.TwoLetterISOLanguageName == "zh";
        var text = new StringBuilder();
        text.Append(LocalServer.Name).Append(": ").Append(report.Server.Command).Append(' ').AppendLine(string.Join(' ', LocalServer.Arguments));
        if (report.Problem is { } problem)
        {
            return text.Append(Tag(ClientOutcome.Failed, zh)).AppendLine(ClientConfigTexts.Problem(problem, zh)).ToString();
        }

        if (report.Clients.Count == 0)
        {
            return text.AppendLine(ClientConfigTexts.NothingRecorded(zh)).ToString();
        }

        foreach (ClientResult result in report.Clients)
        {
            text.Append(Tag(result.Outcome, zh)).Append(result.Client).AppendLine(result.File is null ? string.Empty : "  " + result.File);
            Lines(text, result.Problem is { } issue ? ClientConfigTexts.Problem(issue, zh) : null);
            Lines(text, result.Backup is { } backup ? ClientConfigTexts.Backup(zh) + backup : null);
            Lines(text, result.CommentsDropped ? ClientConfigTexts.CommentsDropped(zh) : null);
            if (result.Outcome == ClientOutcome.WouldWrite)
            {
                Lines(text, ClientConfigTexts.Before(zh));
                Lines(text, result.Before ?? ClientConfigTexts.None(zh), Indent);
                Lines(text, ClientConfigTexts.After(zh));
                Lines(text, result.After, Indent);
            }
        }

        return report.Clients.Any(c => c.Outcome is ClientOutcome.Written or ClientOutcome.Removed)
            ? text.AppendLine(ClientConfigTexts.Restart(zh)).ToString()
            : text.ToString();
    }

    public static string Json(ClientConfigReport report) => new JsonObject
    {
        ["schemaVersion"] = SchemaVersion,
        ["mode"] = report.Mode switch
        {
            ClientConfigMode.Write => "write",
            ClientConfigMode.DryRun => "dry_run",
            ClientConfigMode.Remove => "remove",
            _ => throw new UnreachableException($"Mode {report.Mode} has no JSON name."),
        },
        ["server"] = new JsonObject
        {
            ["name"] = LocalServer.Name,
            ["command"] = report.Server.Command,
            ["args"] = new JsonArray([.. LocalServer.Arguments.Select(a => JsonValue.Create(a))]),
        },
        ["problem"] = Issue(report.Problem),
        ["clients"] = new JsonArray([.. report.Clients.Select(Client)]),
    }.ToJsonString(HostJson.Options);

    private static JsonObject Client(ClientResult result) => new()
    {
        ["client"] = result.Client,
        ["file"] = result.File,
        ["action"] = result.Action == ClientAction.Write ? "write" : "remove",
        ["result"] = result.Outcome switch
        {
            ClientOutcome.Written => "written",
            ClientOutcome.WouldWrite => "would_write",
            ClientOutcome.Removed => "removed",
            ClientOutcome.Unchanged => "unchanged",
            ClientOutcome.Failed => "failed",
            _ => throw new UnreachableException($"Outcome {result.Outcome} has no JSON name."),
        },
        ["before"] = result.Before,
        ["after"] = result.After,
        ["backup"] = result.Backup,
        ["notes"] = result.CommentsDropped
            ? new JsonArray(new JsonObject { ["code"] = "comments_dropped", ["zh"] = ClientConfigTexts.CommentsDropped(zh: true), ["en"] = ClientConfigTexts.CommentsDropped(zh: false) })
            : new JsonArray(),
        ["problem"] = Issue(result.Problem),
    };

    private static JsonObject? Issue(ClientIssue? issue) => issue is null ? null : new JsonObject
    {
        ["code"] = issue.Problem switch
        {
            ClientProblem.InvalidFile => "invalid_file",
            ClientProblem.UnsupportedToml => "unsupported_toml",
            ClientProblem.AmbiguousPackage => "ambiguous_package",
            ClientProblem.FileChanged => "file_changed",
            ClientProblem.InvalidRecord => "invalid_record",
            ClientProblem.RecordFailed => "record_failed",
            ClientProblem.UnknownClient => "unknown_client",
            ClientProblem.IoError => "io_error",
            ClientProblem.NotEplanmcp => "not_eplanmcp",
            _ => throw new UnreachableException($"Problem {issue.Problem} has no JSON name."),
        },
        ["zh"] = ClientConfigTexts.Problem(issue, zh: true),
        ["en"] = ClientConfigTexts.Problem(issue, zh: false),
    };

    private static string Tag(ClientOutcome outcome, bool zh) => "[" + ClientConfigTexts.Outcome(outcome, zh) + "] ";

    /// <summary>Each line of <paramref name="value"/> under the client's line, <paramref name="indent"/> further in.</summary>
    private static void Lines(StringBuilder text, string? value, string indent = "")
    {
        foreach (string line in value?.Split('\n') ?? [])
        {
            text.Append(Indent).Append(indent).AppendLine(line.TrimEnd('\r'));
        }
    }
}
