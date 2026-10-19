using System.Diagnostics;
using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.EServices;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S6;

/// <summary>
/// Spike S6 (read API). Answers the live questions of the M2 design (section 4.6): how long the finder
/// calls take on the sample project, what the different name properties actually contain, what
/// multi-language values look like, whether reading messages or objects has side effects, and how to
/// list the connections of a page.
/// <para>
/// Usage: <c>EplanMcpSpikeS6 /Mode:read|checkrun /Report:"…\x.json" /ScratchRoot:"…\scratch"</c>.
/// Runs only when the current project lies under ScratchRoot. "read" only reads; "checkrun" runs a check,
/// which writes check messages, and is meant for the scratch copy only.
/// </para>
/// </summary>
public sealed class SpikeS6Action : IEplAction
{
    private const int SampleSize = 12;

    // Property ids from the 2026 Properties enums (reflection dump); read by number so names that live in
    // FunctionBase rather than Function need no cast.
    private static readonly (string Name, int Id)[] _functionNameIds =
    [
        ("FUNC_IDENTNAME", 20000),
        ("FUNC_IDENTDEVICETAG", 20005),
        ("FUNC_FULLDEVICETAG", 20006),
        ("FUNC_IDENTDEVICETAGWITHOUTSTRUCTURES", 20008),
        ("FUNC_DEVICETAG_FULL", 20009),
        ("FUNC_DEVICETAG_FULLNAME", 20058),
        ("FUNC_TEXT", 20011),
        ("FUNC_ARTICLE_PARTNR", 20100),
    ];

    private static readonly (string Name, int Id)[] _pageNameIds =
    [
        ("PAGE_NAME", 11000),
        ("PAGE_FULLNAME", 11009),
        ("PAGE_IDENTNAME", 11010),
        ("PAGE_NOMINATIOMN", 11011),
        ("PAGE_TYPE_NUMERIC", 11029),
    ];

    private static readonly (string Name, int Id)[] _connectionIds =
    [
        ("CONNECTION_WIRECROSSSECTION", 31002),
        ("CONNECTION_WIRENUMBER", 31004),
        ("CONNECTION_DESIGNATION", 31011),
        ("CONNECTION_WIRECROSSSECTION_UNIT", 31059),
        ("CONNECTION_MLWIRENUMBER", 31061),
    ];

    private static readonly (string Name, int Id)[] _articleIds =
    [
        ("ARTICLE_PARTNR", 22001),
        ("ARTICLE_DESCR1", 22004),
        ("ARTICLE_MANUFACTURER", 22007),
    ];

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        // Same scheme as S2: a rebuild loaded side by side gets its own action name (api-notes A8).
        string assemblyName = typeof(SpikeS6Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S6";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string reportPath = GetParameter(oActionCallingContext, "Report");
        string scratchRoot = GetParameter(oActionCallingContext, "ScratchRoot");
        string mode = GetParameter(oActionCallingContext, "Mode");
        var report = new JObject
        {
            ["spike"] = "S6-read-api",
            ["mode"] = mode.Length == 0 ? "read" : mode,
            ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
        };

        try
        {
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            using (new LockingStep())
            {
                Run(report, scratchRoot, mode);
            }
        }
        catch (Exception ex)
        {
            report["fatal"] = DescribeException(ex);
        }

        if (!string.IsNullOrEmpty(reportPath))
        {
            File.WriteAllText(reportPath, report.ToString(Formatting.Indented));
        }

        return true;
    }

    private static void Run(JObject report, string scratchRoot, string mode)
    {
        Project? project = new SelectionSet().GetCurrentProject(false);
        if (project is null)
        {
            report["refused"] = "no project open";
            return;
        }

        string root = Path.GetFullPath(scratchRoot).TrimEnd('\\') + "\\";
        if (!Path.GetFullPath(project.ProjectFullName).StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            report["refused"] = "project is outside the scratch root";
            return;
        }

        if (mode == "checkrun")
        {
            Probe(report, "messages_before", () => Messages(project));
            Probe(report, "verify_project", () =>
            {
                using var check = new Check();
                check.VerifyProject(project, string.Empty);
                return "done";
            });
            Probe(report, "messages_after", () => Messages(project));
            return;
        }

        using var finder = new DMObjectsFinder(project);
        Function[] functions = [];
        Page[] pages = [];
        Probe(report, "project", () => DescribeProject(project));
        Probe(report, "pages_all", () => Timed(() => pages = finder.GetPages(null), p => p.Length));
        Probe(report, "pages_sample", () => new JArray(pages.Take(SampleSize).Select(DescribePage)));
        Probe(report, "pages_order", () => DescribeOrder(pages.Select(p => p.Name).ToArray()));
        Probe(report, "pages_filter", () => PageFilters(finder, pages));
        Probe(report, "functions_all", () => Timed(() => functions = finder.GetFunctions(null), f => f.Length));
        Probe(report, "functions_read_names", () => ReadAllNames(functions));
        Probe(report, "functions_categories", () => Histogram(functions.Select(f => (f.IsMainFunction ? "main:" : "aux:") + f.FunctionCategory)));
        Function[] samples = PickSamples(functions);
        JArray locksBefore = Locks(samples);
        Probe(report, "functions_sample", () => new JArray(samples.Select(DescribeFunction)));
        Probe(report, "functions_filter", () => FunctionFilters(finder, samples, pages));
        Probe(report, "function_all_properties", () => AllProperties(samples.FirstOrDefault(f => f.IsMainFunction)));
        Probe(report, "connections", () => Connections(finder, samples, pages));
        Probe(report, "article_references", () => ArticleReferences(finder, functions, project));
        Probe(report, "project_messages", () => Messages(project));
        Probe(report, "system_messages", SystemMessages);
        Probe(report, "locks", () => new JObject { ["before"] = locksBefore, ["after"] = Locks(samples), ["projectIsLocked"] = project.IsLocked });
    }

    private static JObject DescribeProject(Project project)
    {
        return new JObject
        {
            ["name"] = project.ProjectName,
            ["isReadOnly"] = project.IsReadOnly,
            ["currentUsers"] = project.CurrentUsers?.Length,
            ["PROJ_NAME"] = Value(project.Properties[10000]),
            ["PROJ_PAGECOUNT"] = Value(project.Properties[10300]),
            ["PROJ_SOURCELANGUAGE"] = Value(project.Properties[10500]),
        };
    }

    private static JObject DescribePage(Page page)
    {
        var o = new JObject
        {
            ["Name"] = page.Name,
            ["IdentifyingName"] = page.IdentifyingName,
            ["PageType"] = page.PageType.ToString(),
            ["PageTypeName"] = page.PageTypeName,
        };
        foreach ((string name, int id) in _pageNameIds)
        {
            o[name] = Value(page.Properties[id]);
        }

        return o;
    }

    private static JObject PageFilters(DMObjectsFinder finder, Page[] pages)
    {
        var o = new JObject();
        Page? first = pages.FirstOrDefault(p => p.Name.Contains('/'));
        if (first is null)
        {
            return o;
        }

        string prefix = first.Name.Substring(0, first.Name.LastIndexOf('/'));
        o["prefix"] = prefix;
        o["byPrefix"] = Timed(() => finder.GetPages(new PagesFilter { Name = prefix }), p => p.Length);
        o["byPrefixLowerCase"] = Timed(() => finder.GetPages(new PagesFilter { Name = prefix.ToLowerInvariant() }), p => p.Length);
        o["exactFirstName"] = Timed(() => finder.GetPages(new PagesFilter { Name = first.Name, ExactNameMatching = true }), p => p.Length);
        o["inMemoryPrefixCount"] = pages.Count(p => p.Name.StartsWith(prefix, StringComparison.Ordinal));
        return o;
    }

    private static JObject ReadAllNames(Function[] functions)
    {
        // Everything a device list row needs: both names, main or auxiliary, category.
        var watch = Stopwatch.StartNew();
        int nonEmpty = 0;
        foreach (Function f in functions)
        {
            string name = f.Name;
            string visible = f.VisibleName;
            bool main = f.IsMainFunction;
            string category = f.FunctionCategory.ToString();
            if (name.Length > 0 && visible.Length > 0 && (main || category.Length > 0))
            {
                nonEmpty++;
            }
        }

        long namesMs = watch.ElapsedMilliseconds;
        watch.Restart();
        int withText = functions.Count(f => !f.Properties[20011].IsEmpty);
        return new JObject { ["count"] = functions.Length, ["namesMs"] = namesMs, ["withNames"] = nonEmpty, ["funcTextMs"] = watch.ElapsedMilliseconds, ["withFuncText"] = withText };
    }

    private static Function[] PickSamples(Function[] functions)
    {
        // One main and one auxiliary function per category, so names of contactors, terminals, PLC
        // boxes, cables and the like all show up.
        return functions
            .GroupBy(f => (f.IsMainFunction, f.FunctionCategory))
            .Select(g => g.First())
            .OrderBy(f => f.IsMainFunction ? 0 : 1)
            .Take(SampleSize * 2)
            .ToArray();
    }

    private static JObject DescribeFunction(Function f)
    {
        var o = new JObject
        {
            ["Name"] = f.Name,
            ["VisibleName"] = f.VisibleName,
            ["IsMainFunction"] = f.IsMainFunction,
            ["FunctionCategory"] = f.FunctionCategory.ToString(),
            ["Page"] = Try(() => f.Page?.Name),
            ["ArticleReferences"] = Try(() => f.ArticleReferences.Length),
            ["SubFunctions"] = Try(() => f.IsMainFunction ? f.SubFunctions.Length : (int?)null),
            ["Pins"] = Try(() => string.Join(", ", f.Pins.Take(4).Select(p => p.Name))),
        };
        foreach ((string name, int id) in _functionNameIds)
        {
            o[name] = Value(f.Properties[id]);
        }

        return o;
    }

    private static JObject FunctionFilters(DMObjectsFinder finder, Function[] samples, Page[] pages)
    {
        var o = new JObject();
        Function? main = samples.FirstOrDefault(f => f.IsMainFunction && f.Name.Length > 0);
        if (main is not null)
        {
            o["sampleName"] = main.Name;
            o["sampleVisibleName"] = main.VisibleName;
            o["byName"] = Timed(() => finder.GetFunctions(new FunctionsFilter { Name = main.Name }), f => f.Length);
            o["byNameExact"] = Timed(() => finder.GetFunctions(new FunctionsFilter { Name = main.Name, ExactNameMatching = true }), f => f.Length);
            o["byVisibleName"] = Timed(() => finder.GetFunctions(new FunctionsFilter { Name = main.VisibleName }), f => f.Length);
            o["byCategory"] = Timed(() => finder.GetFunctions(new FunctionsFilter { FunctionCategory = main.FunctionCategory }), f => f.Length);
        }

        Page? page = pages.FirstOrDefault(p => p.PageType.ToString().IndexOf("Circuit", StringComparison.OrdinalIgnoreCase) >= 0) ?? pages.FirstOrDefault();
        if (page is not null)
        {
            o["page"] = page.Name;
            o["byPage"] = Timed(() => finder.GetFunctions(new FunctionsFilter { Page = page }), f => f.Length);
            o["byPageMain"] = Timed(() => finder.GetFunctions(new FunctionsFilter { Page = page }).Count(f => f.IsMainFunction), n => n);
        }

        return o;
    }

    private static JObject AllProperties(Function? f)
    {
        if (f is null)
        {
            return new JObject();
        }

        var watch = Stopwatch.StartNew();
        AnyPropertyId[] ids = f.Properties.ExistingIds;
        PropertyValue[] values = f.Properties.ExistingValues;
        long ms = watch.ElapsedMilliseconds;
        var sample = new JArray(ids.Zip(values, (id, value) => new JObject { ["id"] = id.AsInt, ["value"] = Value(value) }).Take(25));
        return new JObject { ["function"] = f.Name, ["count"] = ids.Length, ["ms"] = ms, ["sample"] = sample };
    }

    private static JObject Connections(DMObjectsFinder finder, Function[] samples, Page[] pages)
    {
        var o = new JObject();
        Connection[] all = [];
        o["all"] = Timed(() => all = finder.GetConnections(null), c => c.Length);
        o["sample"] = new JArray(all.Take(6).Select(c =>
        {
            var e = new JObject
            {
                ["start"] = Try(() => c.StartPin?.ParentFunction?.Name + ":" + c.StartPin?.Name),
                ["end"] = Try(() => c.EndPin?.ParentFunction?.Name + ":" + c.EndPin?.Name),
                ["page"] = Try(() => c.Page?.Name),
            };
            foreach ((string name, int id) in _connectionIds)
            {
                e[name] = Value(c.Properties[id]);
            }

            return e;
        }));

        Function? main = samples.FirstOrDefault(f => f.IsMainFunction);
        if (main is not null)
        {
            o["byFunction"] = Timed(() => finder.GetConnections(new ConnectionsFilter { Function = main }), c => c.Length);
        }

        Page? page = all.Select(c => Try2(() => c.Page)).FirstOrDefault(p => p is not null);
        if (page is not null)
        {
            o["page"] = page.Name;
#pragma warning disable CS0618 // the spike measures the obsolete filter against its alternatives
            o["byPageObsoleteFilter"] = Timed(() => finder.GetConnections(new ConnectionsFilter { Page = page }), c => c.Length);
#pragma warning restore CS0618
            o["byPageInMemory"] = Timed(() => all.Count(c => page.Equals(Try2(() => c.Page))), n => n);
            o["byPageViaFunctions"] = Timed(
                () => finder.GetFunctions(new FunctionsFilter { Page = page }).SelectMany(f => Try2(() => f.Connections) ?? []).Distinct().Count(),
                n => n);
        }

        return o;
    }

    private static JObject ArticleReferences(DMObjectsFinder finder, Function[] functions, Project project)
    {
        var o = new JObject();
        ArticleReference[] refs = [];
        o["finderAll"] = Timed(() => refs = finder.GetArticleReferences(null), r => r.Length);
        o["sumOverFunctions"] = Timed(() => functions.Sum(f => Try2(() => f.ArticleReferences.Length)), n => n);
        o["sample"] = new JArray(refs.Take(5).Select(r => new JObject
        {
            ["PartNr"] = r.PartNr,
            ["VariantNr"] = Try(() => r.VariantNr),
            ["Count"] = Try(() => r.Count),
            ["parent"] = Try(() => r.ParentObject?.GetType().Name),
        }));
        Article[] articles = [];
        o["projectArticles"] = Timed(() => articles = project.Articles, a => a.Length);
        o["articleSample"] = new JArray(articles.Take(4).Select(a =>
        {
            var e = new JObject();
            foreach ((string name, int id) in _articleIds)
            {
                e[name] = Value(a.Properties[id]);
            }

            return e;
        }));
        return o;
    }

    private static JObject Messages(Project project)
    {
        var watch = Stopwatch.StartNew();
        var messages = new PrjMessagesCollection(project);
        int count = messages.Count;
        long ctorMs = watch.ElapsedMilliseconds;
        var types = new Dictionary<string, int>();
        var categories = new Dictionary<string, int>();
        var sample = new JArray();
        int seen = 0;
        foreach (object item in messages)
        {
            Increment(types, item.GetType().Name);
            if (item is ProjectMessage m)
            {
                Increment(categories, m.GetCategory().ToString());
                if (sample.Count < 5)
                {
                    sample.Add(new JObject
                    {
                        ["id"] = m.GetId(),
                        ["category"] = m.GetCategory().ToString(),
                        ["group"] = m.GetGroup().ToString(),
                        ["text"] = m.GetText(),
                        ["done"] = m.GetDone(),
                        ["time"] = m.GetTime().ToString("o", CultureInfo.InvariantCulture),
                        ["object1"] = Try(() => m.GetObject1()?.GetType().Name),
                    });
                }
            }

            if (++seen >= 5000)
            {
                break;
            }
        }

        return new JObject
        {
            ["count"] = count,
            ["ctorMs"] = ctorMs,
            ["enumerateMs"] = watch.ElapsedMilliseconds - ctorMs,
            ["types"] = JObject.FromObject(types),
            ["categories"] = JObject.FromObject(categories),
            ["sample"] = sample,
        };
    }

    private static JToken SystemMessages()
    {
        var o = new JObject();
        var watch = Stopwatch.StartNew();
        var all = new SysMessagesCollection();
        var levels = new Dictionary<string, int>();
        foreach (object item in all)
        {
            if (item is BaseException e)
            {
                Increment(levels, e.MessageLevel.ToString());
            }
        }

        o["all"] = new JObject { ["count"] = all.Count, ["ms"] = watch.ElapsedMilliseconds, ["levels"] = JObject.FromObject(levels) };
        o["fromBookmark0Warning"] = Try(() => new SysMessagesCollection(0, MessageLevel.Warning).Count);
        o["fromBookmark0Error"] = Try(() => new SysMessagesCollection(0, MessageLevel.Error).Count);
        o["last"] = new JArray(all.Cast<object>().OfType<BaseException>().Reverse().Take(3).Select(e => new JObject
        {
            ["level"] = e.MessageLevel.ToString(),
            ["index"] = e.GetMessageIndex(),
            ["text"] = e.GetMessageText(),
            ["occurrences"] = e.NumberOfOccurrences,
        }));
        return o;
    }

    private static JArray Locks(Function[] samples) =>
        new(samples.Take(6).Select(f => new JObject { ["name"] = f.Name, ["isLocked"] = Try(() => f.IsLocked) }));

    private static JObject DescribeOrder(string[] names)
    {
        bool ordinal = names.Zip(names.Skip(1), (a, b) => string.CompareOrdinal(a, b) <= 0).All(x => x);
        bool ignoreCase = names.Zip(names.Skip(1), (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0).All(x => x);
        return new JObject { ["sortedOrdinal"] = ordinal, ["sortedIgnoreCase"] = ignoreCase, ["first"] = new JArray(names.Take(5)), ["last"] = new JArray(names.Reverse().Take(3)) };
    }

    private static JObject Histogram(IEnumerable<string> keys) =>
        JObject.FromObject(keys.GroupBy(k => k).OrderByDescending(g => g.Count()).Take(30).ToDictionary(g => g.Key, g => g.Count()));

    /// <summary>Raw and per-language views of a property value: the shape is one of the S6 questions.</summary>
    private static JToken Value(PropertyValue value)
    {
        try
        {
            if (value.IsEmpty)
            {
                return "<empty>";
            }

            var o = new JObject { ["text"] = Try(() => value.ToString()) };
            MultiLangString? ml = Try2(() => value.ToMultiLangString());
            if (ml is not null)
            {
                o["raw"] = Try(() => ml.GetAsString());
                o["zh_CN"] = Try(() => ml.GetString(ISOCode.Language.L_zh_CN));
                o["en_US"] = Try(() => ml.GetString(ISOCode.Language.L_en_US));
                o["display_zh_CN"] = Try(() => ml.GetStringToDisplay(ISOCode.Language.L_zh_CN));
            }

            return o;
        }
        catch (Exception ex)
        {
            return DescribeException(ex);
        }
    }

    private static JObject Timed<T>(Func<T> read, Func<T, int> count)
    {
        var watch = Stopwatch.StartNew();
        T result = read();
        return new JObject { ["count"] = count(result), ["ms"] = watch.ElapsedMilliseconds };
    }

    private static JToken Try<T>(Func<T> read)
    {
        try
        {
            return read() is { } value ? JToken.FromObject(value) : JValue.CreateNull();
        }
        catch (Exception ex)
        {
            return DescribeException(ex);
        }
    }

    private static T? Try2<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return default;
        }
    }

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

    private static void Probe(JObject report, string name, Func<JToken> probe)
    {
        var watch = Stopwatch.StartNew();
        JObject entry;
        try
        {
            entry = new JObject { ["ok"] = true, ["result"] = probe() };
        }
        catch (Exception ex)
        {
            entry = new JObject { ["ok"] = false, ["error"] = DescribeException(ex) };
        }

        entry["ms"] = watch.ElapsedMilliseconds;
        report[name] = entry;
    }

    private static JObject DescribeException(Exception ex)
    {
        var o = new JObject { ["type"] = ex.GetType().FullName, ["message"] = ex.Message };
        if (ex.InnerException is not null)
        {
            o["inner"] = DescribeException(ex.InnerException);
        }

        return o;
    }

    private static string GetParameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        return value ?? string.Empty;
    }
}
