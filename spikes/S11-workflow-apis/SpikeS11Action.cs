using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.EServices;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S11;

/// <summary>
/// Spike S11 (M4 design 4.6): the APIs behind eplan_check_run, eplan_report_update and the export tools, run the way the
/// Bridge runs a tool (main thread through the dispatcher, after the scheduling action has returned, writes in
/// SafetyPoint + UndoStep + QuietModeStep), as S10 did: what each changes, how long it takes, whether a SafetyPoint
/// takes it back, where exports put their files.
/// <para>
/// Usage: <c>EplanMcpSpikeS11 /Mode:projects|select|restore|count|check|checkrollback|checkpages|reports|reportsrollback|reportpage|exportpdf|exportpdfall|exportdxf|close
/// /Report:"…\x.json" /ScratchRoot:"…\s11" [/Out:"…\dir"] [/Restore:name]</c>. Works only on an open project under
/// ScratchRoot, found by path.
/// </para>
/// </summary>
public sealed class SpikeS11Action : IEplAction
{
    // Report (evaluation) page types are the DocumentType values 9-33 and 35-37 (reflection), plus a few later ones.
    private static readonly int[] _reportTypes = [.. Enumerable.Range(9, 25), 35, 36, 37, 39, 41, 42, 44, 45, 46, 47, 48, 50, 52, 55, 56, 59, 60, 62, 63];

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS11Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S11";
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
            Out = Parameter(oActionCallingContext, "Out"),
            Restore = Parameter(oActionCallingContext, "Restore"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S11-workflow-apis", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
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
        // Ctrl+Z works on the current project: the driver undoes only while every open project is a scratch copy and the
        // spike's copy is current (S10).
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
                report["current"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
                break;
            case "count":
                report["state"] = State(project);
                break;
            case "check":
                report["check"] = Scoped("S11 check", () => Timed(() =>
                {
                    int before = Messages(project);
                    new Check().VerifyProject(project, string.Empty, false);
                    return new JObject { ["messagesBefore"] = before, ["messagesAfter"] = Messages(project) };
                }));
                break;
            case "checkrollback":
                report["check"] = RolledBack(() =>
                {
                    int before = Messages(project);
                    new Check().VerifyProject(project, string.Empty, false);
                    return new JObject { ["messagesBefore"] = before, ["messagesDuring"] = Messages(project) };
                }, () => new JObject { ["messagesAfter"] = Messages(project) });
                break;
            case "checkpages":
                report["check"] = Scoped("S11 check pages", () => Timed(() =>
                {
                    int before = Messages(project);
                    Page[] two = [.. Pages(project).Where(pg => pg.PageType == DocumentTypeManager.DocumentType.Circuit).Take(2)];
                    new Check().VerifyPages(new ArrayList(two), string.Empty);
                    return new JObject { ["pages"] = new JArray(two.Select(pg => pg.Name)), ["messagesBefore"] = before, ["messagesAfter"] = Messages(project) };
                }));
                break;
            case "reports":
                report["reports"] = Scoped("S11 reports", () => Timed(() => ReportsChange(project, () => new Reports().GenerateProject(project))));
                break;
            case "reportsrollback":
                report["reports"] = RolledBack(() => ReportsChange(project, () => new Reports().GenerateProject(project)), () => new JObject { ["state"] = State(project) });
                break;
            case "reportpage":
                Page? toc = Pages(project).FirstOrDefault(pg => pg.PageType == DocumentTypeManager.DocumentType.TableOfContents);
                report["page"] = toc?.Name;
                report["reports"] = toc is null ? "no table of contents page" : Scoped("S11 report page", () => Timed(() => ReportsChange(project, () => new Reports().Update(toc))));
                break;
            case "exportpdf":
                report["pdfLastUsedBefore"] = LastUsed("USER.PDFExportGUI.SCHEMAS");
                report["export"] = Timed(() =>
                {
                    Page[] three = [.. Pages(project).Where(pg => pg.PageType == DocumentTypeManager.DocumentType.Circuit).Take(3)];
                    new Export().PdfPages(new ArrayList(three), string.Empty, Path.Combine(p.Out, "s11-three"), Export.DegreeOfColor.Color, false, string.Empty, false);
                    return new JObject { ["pages"] = new JArray(three.Select(pg => pg.Name)) };
                });
                report["pdfLastUsedAfter"] = LastUsed("USER.PDFExportGUI.SCHEMAS");
                break;
            case "exportpdfall":
                report["export"] = Timed(() =>
                {
                    Page[] all = Pages(project);
                    new Export().PdfPages(new ArrayList(all), string.Empty, Path.Combine(p.Out, "s11-all"), Export.DegreeOfColor.Color, false, string.Empty, false);
                    return new JObject { ["pages"] = all.Length };
                });
                break;
            case "exportdxf":
                report["dxfLastUsedBefore"] = LastUsed("USER.DXF.SCHEMES");
                report["export"] = Timed(() =>
                {
                    Page[] two = [.. Pages(project).Where(pg => pg.PageType == DocumentTypeManager.DocumentType.Circuit).Take(2)];
                    new Export().DxfDwgPagesToDisk(two, string.Empty, p.Out, string.Empty);
                    return new JObject { ["pages"] = new JArray(two.Select(pg => pg.Name)) };
                });
                report["dxfLastUsedAfter"] = LastUsed("USER.DXF.SCHEMES");
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

    private static JObject ReportsChange(Project project, System.Action change)
    {
        Dictionary<string, string> before = Pages(project).ToDictionary(pg => pg.Name, pg => pg.PageType.ToString(), StringComparer.Ordinal);
        change();
        Dictionary<string, string> after = Pages(project).ToDictionary(pg => pg.Name, pg => pg.PageType.ToString(), StringComparer.Ordinal);
        return new JObject
        {
            ["pagesBefore"] = before.Count,
            ["pagesAfter"] = after.Count,
            ["reportPagesBefore"] = before.Count(pg => IsReport(pg.Value)),
            ["reportPagesAfter"] = after.Count(pg => IsReport(pg.Value)),
            ["added"] = new JArray(after.Keys.Except(before.Keys).Take(30)),
            ["removed"] = new JArray(before.Keys.Except(after.Keys).Take(30)),
        };
    }

    private static JObject State(Project project)
    {
        Page[] pages = Pages(project);
        return new JObject
        {
            ["pages"] = pages.Length,
            ["reportPages"] = pages.Count(pg => IsReport(pg.PageType.ToString())),
            ["messages"] = Messages(project),
        };
    }

    private static bool IsReport(string type) =>
        Enum.TryParse(type, out DocumentTypeManager.DocumentType parsed) && _reportTypes.Contains((int)parsed);

    /// <summary>The Bridge's TransactionScope (M3 design 4.2): SafetyPoint, the UndoStep inside it, commit, close.</summary>
    private static JToken Scoped(string description, Func<JToken> change)
    {
        using SafetyPoint safety = SafetyPoint.Create();
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        JToken result;
        using (new QuietModeStep(QuietModes.ShowNoDialogs))
        {
            result = change();
        }

        safety.Commit();
        undo.SetUndoDescription(description);
        undo.CloseUndo();
        return result;
    }

    private static JToken RolledBack(Func<JObject> change, Func<JObject> afterwards)
    {
        JObject result;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            result = (JObject)Timed(change);
            safety.Rollback();
        }

        result["afterRollback"] = afterwards();
        return result;
    }

    private static JToken Timed(Func<JObject> work)
    {
        var clock = Stopwatch.StartNew();
        JObject result = work();
        result["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2);
        return result;
    }

    private static int Messages(Project project) => new PrjMessagesCollection(project).Count;

    private static JToken LastUsed(string node) => Try(() =>
    {
        var scheme = new SchemeSetting();
        scheme.Init(node);
        return scheme.GetLastUsed();
    });

    private static Page[] Pages(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null);
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

        public string Out { get; set; } = string.Empty;

        public string Restore { get; set; } = string.Empty;
    }
}
