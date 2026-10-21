using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S23;

/// <summary>
/// Spike S23 (M4c design 4.6, 4.7, 4.9): translating a project, setting and removing project languages, and the
/// revision state, run the way the Bridge runs a tool (main thread through the dispatcher, after the scheduling action
/// has returned, writes in SafetyPoint + UndoStep + QuietModeStep), as S21 did. Every write is on a scratch copy;
/// completing a revision, which makes a project read-only, is not tried.
/// <para>
/// Usage: <c>EplanMcpSpikeS23 /Mode:projects|select|restore|languages|texts|translaterollback|translate|displayrollback|removerollback|revisionstate|createrevision|close
/// /Report:"…\x.json" /ScratchRoot:"…\s23" [/Restore:name]</c>. Works only on an open project under ScratchRoot.
/// </para>
/// </summary>
public sealed class SpikeS23Action : IEplAction
{
    private const string RemovedLanguage = "sv_SE";

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS23Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S23";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        var p = new Params
        {
            Report = Parameter(oActionCallingContext, "Report"),
            ScratchRoot = Parameter(oActionCallingContext, "ScratchRoot"),
            Mode = Parameter(oActionCallingContext, "Mode"),
            Restore = Parameter(oActionCallingContext, "Restore"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S23-translation-revisions", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
        try
        {
            Thread.Sleep(300); // let the scheduling action return first
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(900), new Func<bool>(() =>
            {
                // Everything is caught in here: an exception escaping a dispatched delegate ends EPLAN (S1).
                try
                {
                    using (new LockingStep())
                    using (new QuietModeStep(QuietModes.ShowNoDialogs))
                    {
                        Run(report, p);
                    }
                }
                catch (Exception ex)
                {
                    report["fatal"] = Describe(ex);
                }

                return true;
            }));
            if (done is null)
            {
                report["fatal"] = "the dispatcher did not run the probes within 900 s";
            }
        }
        catch (Exception ex)
        {
            report["fatal"] = Describe(ex);
        }

        File.WriteAllText(p.Report + ".partial", report.ToString(Formatting.Indented));
        File.Delete(p.Report);
        File.Move(p.Report + ".partial", p.Report);
    }

    private static void Run(JObject report, Params p)
    {
        Project[] open = OpenProjects();
        string scratchHome = Path.GetDirectoryName(Path.GetDirectoryName(p.ScratchRoot.TrimEnd('\\'))) ?? p.ScratchRoot;
        report["openOutsideScratch"] = open.Count(o => !IsUnder(o.ProjectFullName, scratchHome));
        report["current"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
        if (p.Mode == "projects")
        {
            report["open"] = new JArray(open.Select(o => o.ProjectName));
            return;
        }

        if (p.Mode == "restore")
        {
            Project? previous = open.FirstOrDefault(o => o.ProjectName == p.Restore);
            report["restored"] = previous is not null && new Edit().SelectProjectInPagesNavigator(previous);
            return;
        }

        Project? project = open.FirstOrDefault(o => IsUnder(o.ProjectFullName, p.ScratchRoot));
        if (project is null)
        {
            report["refused"] = "no open project under the scratch root";
            return;
        }

        report["project"] = project.ProjectName;
        switch (p.Mode)
        {
            case "select":
                report["select"] = new Edit().SelectProjectInPagesNavigator(project);
                break;
            case "languages":
                report["languages"] = Languages(project);
                break;
            case "texts":
                report["texts"] = Texts(project);
                break;
            case "translaterollback":
                report["translate"] = RolledBack(project, () => new Translate().TranslateObjects([project]));
                break;
            case "translate":
                report["translate"] = Scoped("S23 translate", () => Changed(project, () => new Translate().TranslateObjects([project])));
                break;
            case "displayrollback":
                report["display"] = SettingRolledBack(project, () => new Translate().SetDisplayedLanguages(project, "en_US;zh_CN"));
                break;
            case "removerollback":
                report["remove"] = SettingRolledBack(project, () => new Translate().RemoveProjectLanguage(project, RemovedLanguage));
                break;
            case "revisionstate":
                report["revision"] = RevisionState(project);
                break;
            case "createrevision":
                report["revision"] = Scoped("S23 revision", () =>
                {
                    var clock = Stopwatch.StartNew();
                    string newPath = string.Empty;
                    JToken created = Try(() =>
                    {
                        new Revision().CreateRevision(project, "S23-1", "EplanMcp spike S23", ref newPath);
                        return "created";
                    });
                    return new JObject
                    {
                        ["created"] = created,
                        ["newPath"] = newPath,
                        ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2),
                        ["after"] = RevisionState(project),
                    };
                });
                break;
            case "close":
                project.Close();
                report["close"] = "closed";
                break;
            default:
                report["refused"] = "unknown mode " + p.Mode;
                break;
        }
    }

    private static JObject Languages(Project project)
    {
        var settings = new ProjectSettings(project);
        return new JObject
        {
            ["translation"] = Try(() => settings.GetStringSetting("TRANSLATEGUI.TRANSLATE_LANGUAGES", 0)),
            ["display"] = Try(() => settings.GetStringSetting("TRANSLATEGUI.DISPLAYED_LANGUAGES", 0)),
            ["source"] = Try(() => settings.GetStringSetting("TRANSLATEGUI.SOURCE_LANGUAGE", 0)),
        };
    }

    /// <summary>Every function text and page description, as stored with all its languages, keyed by object.</summary>
    private static Dictionary<string, string> TextMap(Project project)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var finder = new DMObjectsFinder(project);
        foreach (Function f in finder.GetFunctions(null))
        {
            string text = Raw(() => f.Properties.FUNC_TEXT);
            if (text.Length > 0)
            {
                map["F " + f.ToStringIdentifier()] = text;
            }
        }

        foreach (Page page in finder.GetPages(null))
        {
            string text = Raw(() => page.Properties.PAGE_NOMINATIOMN);
            if (text.Length > 0)
            {
                map["P " + page.ToStringIdentifier()] = text;
            }
        }

        return map;
    }

    private static JObject Texts(Project project)
    {
        Dictionary<string, string> map = TextMap(project);
        return new JObject
        {
            ["texts"] = map.Count,
            ["withEnglish"] = map.Values.Count(v => v.Contains("en_US@")),
            ["withChinese"] = map.Values.Count(v => v.Contains("zh_CN@")),
            ["withSwedish"] = map.Values.Count(v => v.Contains(RemovedLanguage + "@")),
            ["digest"] = Digest(map),
            ["samples"] = new JArray(map.Values.Take(3)),
        };
    }

    private static JObject Changed(Project project, System.Action change)
    {
        Dictionary<string, string> before = TextMap(project);
        var clock = Stopwatch.StartNew();
        JToken error = JValue.CreateNull();
        try
        {
            change();
        }
        catch (Exception ex)
        {
            error = Describe(ex);
        }

        double seconds = Math.Round(clock.Elapsed.TotalSeconds, 2);
        Dictionary<string, string> after = TextMap(project);
        string[] changed = [.. after.Where(a => !before.TryGetValue(a.Key, out string? old) || old != a.Value).Select(a => a.Key)];
        return new JObject
        {
            ["seconds"] = seconds,
            ["error"] = error,
            ["changed"] = changed.Length,
            ["samples"] = new JArray(changed.Take(5).Select(k => (before.TryGetValue(k, out string? old) ? old : "(new)") + "  ->  " + after[k])),
            ["digestBefore"] = Digest(before),
            ["digestAfter"] = Digest(after),
        };
    }

    private static JToken RolledBack(Project project, System.Action change)
    {
        JObject result;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            result = Changed(project, change);
            safety.Rollback();
        }

        result["afterRollback"] = Texts(project);
        return result;
    }

    /// <summary>A change to the project's languages in a SafetyPoint that is rolled back: languages and texts during and after.</summary>
    private static JObject SettingRolledBack(Project project, System.Action change)
    {
        var result = new JObject { ["before"] = Languages(project), ["textsBefore"] = Texts(project) };
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            result["change"] = Try(() =>
            {
                change();
                return "done";
            });
            result["during"] = Languages(project);
            result["textsDuring"] = Texts(project);
            safety.Rollback();
        }

        result["afterRollback"] = Languages(project);
        result["textsAfterRollback"] = Texts(project);
        return result;
    }

    private static JObject RevisionState(Project project) => new()
    {
        ["comparisons"] = Try(() => project.Comparisons?.Length),
        ["uncompletedPages"] = Try(() => new Revision().GetUncompletedPages(project)?.Length),
        ["pages"] = Try(() =>
        {
            using var finder = new DMObjectsFinder(project);
            return finder.GetPages(null).Length;
        }),
    };

    /// <summary>The Bridge's TransactionScope (M3 design 4.2): SafetyPoint, the UndoStep inside it, commit, close.</summary>
    private static JToken Scoped(string description, Func<JToken> change)
    {
        using SafetyPoint safety = SafetyPoint.Create();
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        JToken result = change();
        safety.Commit();
        undo.SetUndoDescription(description);
        undo.CloseUndo();
        return result;
    }

    private static string Raw(Func<PropertyValue> read)
    {
        try
        {
            PropertyValue value = read();
            return value.IsEmpty ? string.Empty : value.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string Digest(Dictionary<string, string> map)
    {
        using var sha = SHA256.Create();
        string text = string.Join("\n", map.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => m.Key + "=" + m.Value));
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty).Substring(0, 16);
    }

    private static Project[] OpenProjects()
    {
        using var manager = new ProjectManager();
        bool lockByDefault = manager.LockProjectByDefault;
        manager.LockProjectByDefault = false;
        try
        {
            return manager.OpenProjects;
        }
        finally
        {
            manager.LockProjectByDefault = lockByDefault;
        }
    }

    private static bool IsUnder(string? path, string root) =>
        path is not null && root.Length > 0
        && Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static JToken Try(Func<JToken?> probe)
    {
        try
        {
            return probe() ?? JValue.CreateNull();
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static JObject Describe(Exception ex)
    {
        var o = new JObject { ["type"] = ex.GetType().FullName, ["message"] = ex.Message };
        if (ex.InnerException is not null)
        {
            o["inner"] = Describe(ex.InnerException);
        }

        return o;
    }

    private static string Parameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        return value ?? string.Empty;
    }

    private sealed class Params
    {
        public string Report { get; set; } = string.Empty;

        public string ScratchRoot { get; set; } = string.Empty;

        public string Mode { get; set; } = string.Empty;

        public string Restore { get; set; } = string.Empty;
    }
}
