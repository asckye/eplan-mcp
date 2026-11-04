using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

/// <summary>Keeps every redraw attempt and the unmodified native comparison available for diagnosis.</summary>
internal sealed class SmallProjectRedrawReport(string run, string project)
{
    /// <summary>The redrawn pages: a value computed from any other page is a subset difference.</summary>
    internal IReadOnlySet<string>? ComparedPages { get; set; }

    private static readonly string[] _noteContextFields = ["page", "objectType", "oid", "stage"];
    private readonly List<JObject> _calls = [];
    private readonly List<JObject> _noTool = [];
    private readonly List<JObject> _notes = [];
    private readonly List<JObject> _warnings = [];
    private readonly List<JObject> _pages = [];
    private readonly List<JObject> _infrastructureErrors = [];
    private readonly List<JObject> _missingPropertyPlacements = [];
    private readonly JObject _propertyNames = new();
    private readonly Dictionary<string, double> _pageElapsed = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Page, string Type, string Oid), JObject> _origins = [];
    private readonly List<JObject> _macroVersionDifferences = [];

    internal void RecordOrigin(string page, JObject source, string origin)
    {
        if (origin is not ("macro" or "part" or "symbol_fallback" or "manual_graphics"))
        { throw new ArgumentOutOfRangeException(nameof(origin)); }
        string type = (string?)source["type"] ?? "Page", oid = source["oid"]?.ToString() ?? "";
        _origins[(page, type, oid)] = new JObject { ["page"] = page, ["objectType"] = type, ["oid"] = oid, ["origin"] = origin };
    }

    internal void RecordMacroVersionDifference(string page, JObject source, string requestedMacro, string actualMacro, string reason)
    {
        JObject item = ObjectNote(page, (string?)source["type"] ?? "Page", source["oid"]?.ToString() ?? "", reason);
        item["requestedMacro"] = requestedMacro;
        item["actualMacro"] = actualMacro;
        item["source"] = source.DeepClone();
        _macroVersionDifferences.Add(item);
    }

    internal void RecordPageElapsed(string page, TimeSpan elapsed) => _pageElapsed[page] = elapsed.TotalMilliseconds;

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

    internal void MissingPropertyPlacement(string page, string type, string oid, int? propertyId, int? propertyIndex, string reason)
    {
        JObject note = ObjectNote(page, type, oid, reason);
        note["propertyId"] = propertyId;
        note["propertyIndex"] = propertyIndex;
        _missingPropertyPlacements.Add(note);
        Warning(page, type + " " + oid + ": " + reason);
    }

    internal void InfrastructureError(string stage, string reason) =>
        _infrastructureErrors.Add(new JObject { ["stage"] = stage, ["reason"] = reason });

    internal void AddPage(string name, JObject? source, JObject? target, string? sourceFile = null, string? targetFile = null)
    {
        foreach (JObject? page in new[] { source, target })
        {
            if (page?["propertyNames"] is JObject names)
            {
                foreach (JProperty property in names.Properties())
                { _propertyNames[property.Name] ??= property.Value.DeepClone(); }
            }
        }
        if (source is null)
        {
            Warning(name, "Source export is missing or unreadable; no source object count can be established.");
        }
        if (target is null)
        {
            Warning(name, "Target export is missing or unreadable; available source objects are compared with an empty target.");
        }

        // Do not weaken or repeat the comparer's normalization, matching, or report-page rules here.
        PageExportComparison comparison = PageExportComparer.Compare(source ?? new JObject(), target ?? new JObject(), ComparedPages);
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
            ["originStatistics"] = OriginStatistics(_origins.Values.Where(item => (string?)item["page"] == name)),
            ["pageDifferences"] = new JArray(comparison.PageDifferences.Select(Field)),
            ["groupStructureDifferences"] = new JArray(comparison.PageDifferences.Where(field => IsGroupField(field.Field)).Select(Field)),
            ["subsetDifferenceCount"] = comparison.SubsetDifferenceCount,
            ["subsetDifferences"] = new JArray(comparison.SubsetDifferences.Select(SubsetField)),
            ["reportRows"] = comparison.ReportRows is { } rows ? new JObject
            {
                ["matched"] = rows.Matched,
                ["sourceOnly"] = new JArray(rows.SourceOnly),
                ["targetOnly"] = new JArray(rows.TargetOnly),
            } : null,
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
        // One multilingual property is one difference per object, regardless of language count.
        IEnumerable<string> differentFields = _pages.SelectMany(page =>
            ((JArray)page["pageDifferences"]!).OfType<JObject>().Select(field => RankingField((string)field["field"]!)).Distinct(StringComparer.Ordinal)
            .Concat(((JObject)page["types"]!).Properties().SelectMany(type =>
                ((JArray)type.Value["differentObjects"]!).SelectMany(item => ((JArray)item["fields"]!).OfType<JObject>()
                    .Select(field => RankingField((string)field["field"]!)).Distinct(StringComparer.Ordinal)))));
        var fields = differentFields.GroupBy(field => field, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new JObject { ["field"] = group.Key, ["name"] = PropertyName(group.Key), ["count"] = group.Count() });
        var missingPropertyIds = _missingPropertyPlacements.GroupBy(item => (int?)item["propertyId"])
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key)
            .Select(group => new JObject
            {
                ["propertyId"] = group.Key,
                ["name"] = group.Key is int id ? _propertyNames[id.ToString(CultureInfo.InvariantCulture)]?.DeepClone() : null,
                ["count"] = group.Count(),
            });
        return new JObject
        {
            ["run"] = run,
            ["project"] = project,
            ["matchingRateDefinition"] = "matched / (matched + missing + extra + different); null when no native objects were compared. Subset differences are listed separately and excluded. Report pages use the comparer's report-field comparison.",
            ["positionTolerance"] = PageExportComparer.PositionTolerance,
            ["propertyNames"] = _propertyNames.DeepClone(),
            ["objectOrigins"] = new JArray(_origins.Values.Select(item => item.DeepClone())),
            ["originStatistics"] = OriginStatistics(_origins.Values),
            ["pageOriginStatistics"] = new JArray(_pageElapsed.Keys.Union(_pages.Select(page => (string)page["name"]!), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).Select(page => new JObject
                {
                    ["page"] = page,
                    ["statistics"] = OriginStatistics(_origins.Values.Where(item => (string?)item["page"] == page)),
                })),
            ["macroVersionDifferenceCount"] = _macroVersionDifferences.Count,
            ["macroVersionDifferences"] = new JArray(_macroVersionDifferences.Select(item => item.DeepClone())),
            ["missingPropertyPlacementCount"] = _missingPropertyPlacements.Count,
            ["missingPropertyPlacements"] = new JArray(_missingPropertyPlacements.Select(item => item.DeepClone())),
            ["missingPropertyIds"] = new JArray(missingPropertyIds),
            ["groupStructureDifferenceCount"] = _pages.Sum(page => ((JArray)page["groupStructureDifferences"]!).Count),
            ["groupStructureDifferences"] = new JArray(_pages.SelectMany(page => ((JArray)page["groupStructureDifferences"]!).OfType<JObject>()
                .Select(field => new JObject { ["page"] = page["name"]!.DeepClone(), ["difference"] = field.DeepClone() }))),
            ["ignoredFields"] = new JArray(PageExportComparer.Ignored.Select(rule => new JObject { ["field"] = rule.Member, ["reason"] = rule.Reason })),
            ["subsetFields"] = new JArray(PageExportComparer.SubsetFields.Select(rule => new JObject { ["field"] = rule.Member, ["name"] = rule.Name, ["reason"] = rule.Reason })),
            ["subsetDifferenceCount"] = _pages.Sum(page => (int)page["subsetDifferenceCount"]!),
            ["subsetDifferences"] = new JArray(_pages.SelectMany(page => ((JArray)page["subsetDifferences"]!).OfType<JObject>()
                .Select(field => new JObject { ["page"] = page["name"]!.DeepClone(), ["difference"] = field.DeepClone() }))),
            ["pageTimings"] = new JArray(_pageElapsed.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new JObject
            {
                ["page"] = pair.Key,
                ["elapsedMilliseconds"] = pair.Value,
                ["drawingReadCalls"] = _calls.Count(call => (string?)call["page"] == pair.Key && (string?)call["tool"] == "eplan_page_get"),
            })),
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
        text.AppendLine("Subset-dependent differences are excluded from this rate and all listed below.");
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
        text.AppendLine("## Page timings");
        text.AppendLine();
        text.AppendLine("| Page | Milliseconds | Drawing reads |");
        text.AppendLine("| --- | ---: | ---: |");
        foreach (JObject timing in ((JArray)report["pageTimings"]!).OfType<JObject>())
        { text.AppendLine("| " + Escape((string)timing["page"]!) + " | " + timing["elapsedMilliseconds"] + " | " + timing["drawingReadCalls"] + " |"); }
        text.AppendLine();
        text.AppendLine("## Object origins");
        text.AppendLine();
        text.AppendLine("Shares use classified source objects; each source object has one final origin. Unmapped objects remain in warnings and comparison results.");
        text.AppendLine();
        text.AppendLine("| Page | From macros | From parts | Symbol fallback | Manual graphics | Classified objects | Manual types |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- |");
        AppendOrigins(text, "All pages", (JObject)report["originStatistics"]!);
        foreach (JObject row in ((JArray)report["pageOriginStatistics"]!).OfType<JObject>())
        { AppendOrigins(text, (string)row["page"]!, (JObject)row["statistics"]!); }
        text.AppendLine();
        text.AppendLine("## Macro version differences (" + report["macroVersionDifferenceCount"] + ")");
        text.AppendLine();
        foreach (JObject item in _macroVersionDifferences)
        {
            text.AppendLine("- " + Escape((string)item["page"]!) + " / " + Escape((string)item["objectType"]!) + " " + item["oid"]
                + ": " + Escape((string)item["requestedMacro"]!) + " → " + Escape((string)item["actualMacro"]!) + "; " + Escape((string)item["reason"]!));
        }
        text.AppendLine();
        text.AppendLine("## Subset differences (" + report["subsetDifferenceCount"] + ")");
        text.AppendLine();
        foreach (JObject item in ((JArray)report["subsetDifferences"]!).OfType<JObject>())
        {
            JToken field = item["difference"]!;
            text.AppendLine("- " + Escape((string)item["page"]!) + " / " + Escape((string)field["field"]!) + " (" + Escape((string)field["name"]!)
                + "): " + Escape(field["source"]!.ToString(Newtonsoft.Json.Formatting.None)) + " → "
                + Escape(field["target"]!.ToString(Newtonsoft.Json.Formatting.None)) + "; " + Escape((string)field["reason"]!));
        }
        text.AppendLine();
        text.AppendLine("## Generated report rows (checked per page, not counted as drawn objects)");
        text.AppendLine();
        text.AppendLine("| Page | Matched | Source only | Target only |");
        text.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (JObject page in ((JArray)report["pages"]!).OfType<JObject>().Where(page => page["reportRows"] is JObject))
        {
            JToken rows = page["reportRows"]!;
            text.AppendLine("| " + Escape((string)page["name"]!) + " | " + rows["matched"] + " | " + ((JArray)rows["sourceOnly"]!).Count + " | " + ((JArray)rows["targetOnly"]!).Count + " |");
        }
        text.AppendLine();
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
        text.AppendLine("## Group structure differences (" + report["groupStructureDifferenceCount"] + ")");
        text.AppendLine();
        foreach (JObject item in ((JArray)report["groupStructureDifferences"]!).OfType<JObject>())
        {
            JToken field = item["difference"]!;
            text.AppendLine("- " + Escape((string)item["page"]!) + " / " + Escape((string)field["field"]!) + ": "
                + Escape(field["source"]!.ToString(Newtonsoft.Json.Formatting.None)) + " → "
                + Escape(field["target"]!.ToString(Newtonsoft.Json.Formatting.None)));
        }
        text.AppendLine();
        text.AppendLine("## Missing default property placements (" + report["missingPropertyPlacementCount"] + ")");
        text.AppendLine();
        foreach (JObject item in ((JArray)report["missingPropertyIds"]!).OfType<JObject>())
        {
            text.AppendLine("- " + (item["propertyId"]?.Type == JTokenType.Null ? "Unknown property ID" : item["propertyId"])
            + " " + Escape((string?)item["name"] ?? "") + ": " + item["count"]);
        }
        text.AppendLine();
        text.AppendLine("## Most frequent different fields");
        text.AppendLine();
        foreach (JObject field in ((JArray)report["differentFields"]!).OfType<JObject>())
        {
            text.AppendLine("- " + Escape((string)field["field"]!) + " " + Escape((string?)field["name"] ?? "") + ": " + field["count"]);
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

    private static bool IsGroupField(string field) => field == "groups" || field.StartsWith("groups[", StringComparison.Ordinal);

    private static JObject OriginStatistics(IEnumerable<JObject> origins)
    {
        JObject[] items = origins.ToArray();
        var result = new JObject { ["total"] = items.Length };
        foreach (string origin in new[] { "macro", "part", "symbol_fallback", "manual_graphics" })
        {
            JObject[] selected = items.Where(item => (string?)item["origin"] == origin).ToArray();
            result[origin] = new JObject
            {
                ["count"] = selected.Length,
                ["share"] = items.Length == 0 ? null : (double)selected.Length / items.Length,
                ["types"] = JObject.FromObject(selected.GroupBy(item => (string)item["objectType"]!, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)),
            };
        }
        return result;
    }

    private static void AppendOrigins(StringBuilder text, string page, JObject statistics)
    {
        string Cell(string origin) => statistics[origin]!["count"] + " ("
            + (statistics[origin]!["share"]?.Type == JTokenType.Null ? "N/A" : ((double)statistics[origin]!["share"]!).ToString("P2", CultureInfo.InvariantCulture)) + ")";
        var types = (JObject)statistics["manual_graphics"]!["types"]!;
        text.AppendLine("| " + Escape(page) + " | " + Cell("macro") + " | " + Cell("part") + " | " + Cell("symbol_fallback") + " | " + Cell("manual_graphics") + " | " + statistics["total"]
            + " | " + string.Join(", ", types.Properties().Select(item => item.Name + "=" + item.Value)) + " |");
    }

    private static string RankingField(string field) => Regex.Replace(field, @"\.(?:[a-z]{2}_[A-Z]{2}|\?\?_\?\?)(?=\.|$)", "", RegexOptions.CultureInvariant);

    private string? PropertyName(string field)
    {
        Match match = Regex.Match(field, @"properties(?:\[id=|\.)(\d+)", RegexOptions.CultureInvariant);
        return match.Success ? (string?)_propertyNames[match.Groups[1].Value] : null;
    }

    private static JObject Field(PageExportFieldDifference field) => new()
    {
        ["field"] = field.Field,
        ["source"] = field.Source?.DeepClone(),
        ["target"] = field.Target?.DeepClone(),
    };

    private static JObject SubsetField(PageExportSubsetDifference field) => new()
    {
        ["field"] = field.Field,
        ["name"] = field.Name,
        ["reason"] = field.Reason,
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
