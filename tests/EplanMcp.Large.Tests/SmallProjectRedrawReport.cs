using System.Globalization;
using System.Text;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

/// <summary>Keeps every redraw attempt and the unmodified native comparison available for diagnosis.</summary>
internal sealed class SmallProjectRedrawReport(string run, string project)
{
    private static readonly string[] _noteContextFields = ["page", "objectType", "oid", "stage"];
    private readonly List<JObject> _calls = [];
    private readonly List<JObject> _noTool = [];
    private readonly List<JObject> _notes = [];
    private readonly List<JObject> _warnings = [];
    private readonly List<JObject> _pages = [];
    private readonly List<JObject> _infrastructureErrors = [];

    internal void RecordCall(string page, string tool, IEnumerable<string> objectTypes, IEnumerable<string> objectOids,
        JObject arguments, JObject? result, bool succeeded, string? error = null)
    {
        _calls.Add(new JObject
        {
            ["sequence"] = _calls.Count + 1,
            ["page"] = page,
            ["tool"] = tool,
            ["objectTypes"] = new JArray(objectTypes.Distinct(StringComparer.Ordinal).DefaultIfEmpty("Unknown")),
            ["objectOids"] = new JArray(objectOids),
            ["arguments"] = arguments.DeepClone(),
            ["result"] = result?.DeepClone(),
            ["succeeded"] = succeeded,
            ["error"] = succeeded ? null : error ?? "Tool call failed; see the recorded response.",
        });
    }

    internal void NoTool(string page, string type, string oid, string reason) => _noTool.Add(ObjectNote(page, type, oid, reason));

    internal void Note(string page, string type, string oid, string reason) => _notes.Add(ObjectNote(page, type, oid, reason));

    internal void Warning(string page, string reason) => _warnings.Add(new JObject { ["page"] = page, ["reason"] = reason });

    internal void InfrastructureError(string stage, string reason) =>
        _infrastructureErrors.Add(new JObject { ["stage"] = stage, ["reason"] = reason });

    internal void AddPage(string name, JObject? source, JObject? target, string? sourceFile = null, string? targetFile = null)
    {
        if (source is null)
        {
            Warning(name, "Source export is missing or unreadable; no source object count can be established.");
        }
        if (target is null)
        {
            Warning(name, "Target export is missing or unreadable; available source objects are compared with an empty target.");
        }

        // Do not weaken or repeat the comparer's normalization, matching, or report-page rules here.
        PageExportComparison comparison = PageExportComparer.Compare(source ?? new JObject(), target ?? new JObject());
        var types = new JObject();
        foreach ((string type, PageExportTypeComparison value) in comparison.Types)
        {
            JObject counts = Counts(value.Matched.Count, value.Missing.Count, value.Extra.Count, value.Different.Count);
            counts["matchedObjects"] = new JArray(value.Matched.Select(match => new JObject
            {
                ["source"] = match.Source.DeepClone(),
                ["target"] = match.Target.DeepClone(),
            }));
            counts["missingObjects"] = new JArray(value.Missing.Select(item => item.DeepClone()));
            counts["extraObjects"] = new JArray(value.Extra.Select(item => item.DeepClone()));
            counts["differentObjects"] = new JArray(value.Different.Select(item => new JObject
            {
                ["source"] = item.Source.DeepClone(),
                ["target"] = item.Target.DeepClone(),
                ["fields"] = new JArray(item.Fields.Select(Field)),
            }));
            types[type] = counts;
        }
        bool reportPage = IsReportPage(source) || IsReportPage(target);
        _pages.Add(new JObject
        {
            ["name"] = name,
            ["sourceFile"] = sourceFile,
            ["targetFile"] = targetFile,
            ["sourceAvailable"] = source is not null,
            ["targetAvailable"] = target is not null,
            ["comparisonMode"] = reportPage ? "report fields" : "native objects and page fields",
            ["totals"] = SumCounts(types.Properties().Select(property => (JObject)property.Value)),
            ["types"] = types,
            ["pageDifferences"] = new JArray(comparison.PageDifferences.Select(Field)),
            ["readWarnings"] = new JArray(comparison.Warnings.Select(warning => new JObject
            {
                ["path"] = warning.Path,
                ["error"] = warning.Error.DeepClone(),
            })),
            ["hasDifferences"] = comparison.HasDifferences,
            ["complete"] = source is not null && target is not null && comparison.IsComplete,
        });
    }

    internal JObject ToJson()
    {
        JObject[] failedCalls = _calls.Where(call => !(bool)call["succeeded"]!).ToArray();
        var failures = failedCalls.SelectMany(call => ((JArray)call["objectTypes"]!).Select(type => new
        {
            Call = call,
            Type = (string)type!,
            Tool = (string)call["tool"]!,
            Error = (string)call["error"]!,
        })).GroupBy(item => (item.Tool, item.Error, item.Type))
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key.Tool, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Error, StringComparer.Ordinal).ThenBy(group => group.Key.Type, StringComparer.Ordinal)
            .Select(group => new JObject
            {
                ["tool"] = group.Key.Tool,
                ["reason"] = group.Key.Error,
                ["objectType"] = group.Key.Type,
                ["callCount"] = group.Count(),
                ["callSequences"] = new JArray(group.Select(item => item.Call["sequence"]!.DeepClone())),
                ["pages"] = new JArray(group.Select(item => (string)item.Call["page"]!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            });
        var unsupported = _noTool.GroupBy(item => (string)item["objectType"]!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new JObject
            {
                ["objectType"] = group.Key,
                ["objectCount"] = group.Count(),
                ["reasons"] = new JArray(group.Select(item => (string)item["reason"]!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                ["objects"] = new JArray(group.Select(item => item.DeepClone())),
            });
        IEnumerable<JObject> differentFields = _pages.SelectMany(page => ((JArray)page["pageDifferences"]!).OfType<JObject>()
            .Concat(((JObject)page["types"]!).Properties().SelectMany(type =>
                ((JArray)type.Value["differentObjects"]!).SelectMany(item => ((JArray)item["fields"]!).OfType<JObject>()))));
        var fields = differentFields.GroupBy(field => (string)field["field"]!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new JObject { ["field"] = group.Key, ["count"] = group.Count() });
        return new JObject
        {
            ["run"] = run,
            ["project"] = project,
            ["matchingRateDefinition"] = "matched / (matched + missing + extra + different); null when no native objects were compared. Report pages use the existing comparer's report-field comparison.",
            ["positionTolerance"] = PageExportComparer.PositionTolerance,
            ["ignoredFields"] = new JArray(PageExportComparer.Ignored.Select(rule => new JObject { ["field"] = rule.Member, ["reason"] = rule.Reason })),
            ["totals"] = SumCounts(_pages.Select(page => (JObject)page["totals"]!)),
            ["pages"] = new JArray(_pages.Select(page => page.DeepClone())),
            ["failedToolCallCount"] = failedCalls.Length,
            ["failedToolCalls"] = new JArray(failures),
            ["noToolTypes"] = new JArray(unsupported),
            ["differentFields"] = new JArray(fields),
            ["calls"] = new JArray(_calls.Select(call => call.DeepClone())),
            ["notes"] = new JArray(_notes.Select(note => note.DeepClone())),
            ["warnings"] = new JArray(_warnings.Select(warning => warning.DeepClone())),
            ["infrastructureErrors"] = new JArray(_infrastructureErrors.Select(error => error.DeepClone())),
        };
    }

    internal string ToMarkdown()
    {
        JObject report = ToJson();
        var text = new StringBuilder();
        text.AppendLine("# Small project redraw — " + Escape(run));
        text.AppendLine();
        text.AppendLine("Object matching rate = matched / (matched + missing + extra + different). N/A means no native objects were compared. Report pages retain the existing report-field comparison.");
        text.AppendLine();
        text.AppendLine("| Scope | Type | Matched | Missing | Extra | Different | Match rate |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        AppendCounts(text, "All pages", "TOTAL", (JObject)report["totals"]!);
        text.AppendLine();
        text.AppendLine("| Page | Type | Matched | Missing | Extra | Different | Match rate |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (JObject page in _pages)
        {
            foreach (JProperty type in ((JObject)page["types"]!).Properties())
            {
                AppendCounts(text, (string)page["name"]!, type.Name, (JObject)type.Value);
            }
            AppendCounts(text, (string)page["name"]!, "TOTAL", (JObject)page["totals"]!);
            text.AppendLine();
            text.AppendLine("Page " + Escape((string)page["name"]!) + ": " + Escape((string)page["comparisonMode"]!)
                + "; page-field differences=" + ((JArray)page["pageDifferences"]!).Count.ToString(CultureInfo.InvariantCulture)
                + "; read warnings=" + ((JArray)page["readWarnings"]!).Count.ToString(CultureInfo.InvariantCulture)
                + "; complete=" + page["complete"] + ".");
            text.AppendLine();
            // Repeat the heading after per-page details so the next page remains a valid Markdown table.
            if (!ReferenceEquals(page, _pages.Last()))
            {
                text.AppendLine("| Page | Type | Matched | Missing | Extra | Different | Match rate |");
                text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
            }
        }
        text.AppendLine("## Failed tool calls (" + report["failedToolCallCount"] + ")");
        text.AppendLine();
        text.AppendLine("| Tool | Reason | Object type | Calls |");
        text.AppendLine("| --- | --- | --- | ---: |");
        foreach (JObject failure in ((JArray)report["failedToolCalls"]!).OfType<JObject>())
        {
            text.AppendLine("| " + Escape((string)failure["tool"]!) + " | " + Escape((string)failure["reason"]!)
                + " | " + Escape((string)failure["objectType"]!) + " | " + failure["callCount"] + " |");
        }
        text.AppendLine();
        text.AppendLine("## Objects without a tool");
        text.AppendLine();
        foreach (JObject item in ((JArray)report["noToolTypes"]!).OfType<JObject>())
        {
            text.AppendLine("- " + Escape((string)item["objectType"]!) + ": " + item["objectCount"] + "; "
                + Escape(string.Join("; ", ((JArray)item["reasons"]!).Values<string>())));
        }
        text.AppendLine();
        text.AppendLine("## Most frequent different fields");
        text.AppendLine();
        foreach (JObject field in ((JArray)report["differentFields"]!).OfType<JObject>())
        {
            text.AppendLine("- " + Escape((string)field["field"]!) + ": " + field["count"]);
        }
        AppendNotes(text, "Notes", _notes);
        AppendNotes(text, "Warnings", _warnings);
        AppendNotes(text, "Infrastructure errors", _infrastructureErrors);
        text.AppendLine();
        text.AppendLine("The JSON report retains every tool input and response, native object comparisons, page-field differences, and read warnings.");
        return text.ToString();
    }

    private static JObject ObjectNote(string page, string type, string oid, string reason) => new()
    {
        ["page"] = page,
        ["objectType"] = type,
        ["oid"] = oid,
        ["reason"] = reason,
    };

    private static JObject Field(PageExportFieldDifference field) => new()
    {
        ["field"] = field.Field,
        ["source"] = field.Source?.DeepClone(),
        ["target"] = field.Target?.DeepClone(),
    };

    private static bool IsReportPage(JObject? page) => (bool?)page?["isReportPage"] == true || page?["report"] is JContainer { Count: > 0 };

    private static JObject Counts(int matched, int missing, int extra, int different)
    {
        long total = (long)matched + missing + extra + different;
        return new JObject
        {
            ["matched"] = matched,
            ["missing"] = missing,
            ["extra"] = extra,
            ["different"] = different,
            ["matchingRate"] = total == 0 ? null : (double)matched / total,
        };
    }

    private static JObject SumCounts(IEnumerable<JObject> rows)
    {
        JObject[] all = rows.ToArray();
        return Counts(all.Sum(row => (int)row["matched"]!), all.Sum(row => (int)row["missing"]!),
            all.Sum(row => (int)row["extra"]!), all.Sum(row => (int)row["different"]!));
    }

    private static void AppendCounts(StringBuilder text, string page, string type, JObject counts) =>
        text.AppendLine("| " + Escape(page) + " | " + Escape(type) + " | " + counts["matched"] + " | " + counts["missing"]
            + " | " + counts["extra"] + " | " + counts["different"] + " | "
            + (counts["matchingRate"]?.Type == JTokenType.Null ? "N/A" : ((double)counts["matchingRate"]!).ToString("P2", CultureInfo.InvariantCulture)) + " |");

    private static void AppendNotes(StringBuilder text, string heading, IEnumerable<JObject> notes)
    {
        text.AppendLine();
        text.AppendLine("## " + heading);
        text.AppendLine();
        foreach (JObject note in notes)
        {
            string context = string.Join(" / ", _noteContextFields
                .Select(key => (string?)note[key]).Where(value => !string.IsNullOrEmpty(value)));
            text.AppendLine("- " + Escape(context) + ": " + Escape((string)note["reason"]!));
        }
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", "", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
}
