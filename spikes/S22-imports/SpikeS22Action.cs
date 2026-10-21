using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S22;

/// <summary>
/// Spike S22 (M4c design 4.5, 4.9): the imports of the third M4c batch, run the way the Bridge runs a tool (main thread
/// through the dispatcher, after the scheduling action has returned, writes in SafetyPoint + UndoStep + QuietModeStep).
/// Inputs are files the driver put in the spike's <c>in\</c> folder under the scratch root: a DXF drawing, and a parts
/// list and a PXF file that this spike exports from the scratch copy first. A PXF import creates a new project, only
/// ever under the scratch root.
/// <para>
/// Usage: <c>EplanMcpSpikeS22 /Mode:projects|select|restore|pages|dxfrollback|dxf|count|partsexport|partsrollback|pxfexport|pxfimport|close
/// /Report:"…\x.json" /ScratchRoot:"…\s22" /In:"…\in" [/Restore:name]</c>. Works only on an open project under ScratchRoot.
/// </para>
/// </summary>
public sealed class SpikeS22Action : IEplAction
{
    private const int DxfPage = 92;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS22Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S22";
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
            In = Parameter(oActionCallingContext, "In"),
            Restore = Parameter(oActionCallingContext, "Restore"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S22-imports", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
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
        report["open"] = new JArray(open.Select(o => o.ProjectName));
        if (p.Mode == "projects")
        {
            return;
        }

        if (p.Mode == "restore")
        {
            Project? previous = open.FirstOrDefault(o => o.ProjectName == p.Restore);
            report["restored"] = previous is not null && new Edit().SelectProjectInPagesNavigator(previous);
            return;
        }

        Project? project = open.FirstOrDefault(o => IsUnder(o.ProjectFullName, p.ScratchRoot) && !IsUnder(o.ProjectFullName, Path.Combine(p.ScratchRoot, "imported")));
        if (project is null)
        {
            report["refused"] = "no open project under the scratch root";
            return;
        }

        report["project"] = project.ProjectName;
        string dxf = Path.Combine(p.In, "rittal.dxf");
        string parts = Path.Combine(p.In, "parts.xml");
        string pxf = Path.Combine(p.In, "s22");
        switch (p.Mode)
        {
            case "select":
                report["select"] = new Edit().SelectProjectInPagesNavigator(project);
                break;
            case "pages":
                report["pages"] = Scoped("S22 page", () => new JArray(NewPage(project, DxfPage).Name));
                break;
            case "dxfrollback":
                report["dxf"] = PlacementsRolledBack(project, () => ImportDxf(dxf, PageNamed(project, DxfPage)));
                break;
            case "dxf":
                report["dxf"] = Scoped("S22 dxf", () => Placements(project, () => ImportDxf(dxf, PageNamed(project, DxfPage))));
                break;
            case "count":
                report["count"] = new JObject { ["dxfPagePlacements"] = PageNamed(project, DxfPage).AllPlacements.Length, ["parts"] = PartsState(project) };
                break;
            case "partsexport":
                report["export"] = Timed(() => new PartsService().ExportPartsList(project, parts, PartsService.Format.XML));
                break;
            case "partsrollback":
            {
                var result = new JObject { ["before"] = PartsState(project) };
                using (SafetyPoint safety = SafetyPoint.Create())
                {
                    result["import"] = Timed(() => new PartsService().ImportPartsList(project, parts, PartsService.Format.XML));
                    result["during"] = PartsState(project);
                    safety.Rollback();
                }

                result["afterRollback"] = PartsState(project);
                report["parts"] = result;
                break;
            }

            case "pxfexport":
                report["export"] = Timed(() => new Export().PXFProject(project, pxf, false, true));
                break;
            case "pxfimport":
            {
                // r0 showed the second argument is the new project's path without extension, not a folder: "…\imported" became
                // imported.elk and imported.edb beside the folder. So the project is named here, inside a folder of its own.
                string destination = Path.Combine(p.ScratchRoot, "imported");
                Directory.CreateDirectory(destination);
                report["import"] = Timed(() => new Import().PXFProject(pxf + ".epj", Path.Combine(destination, "S22_Imported")));
                report["destination"] = new JArray(Directory.EnumerateFileSystemEntries(destination).Select(Path.GetFileName));
                Project[] after = OpenProjects();
                report["openAfter"] = new JArray(after.Select(o => o.ProjectName + " | " + o.ProjectFullName));
                foreach (Project imported in after.Where(o => IsUnder(o.ProjectFullName, destination)))
                {
                    report["importedPages"] = Try(() =>
                    {
                        using var finder = new DMObjectsFinder(imported);
                        return finder.GetPages(null).Length;
                    });
                    imported.Close();
                    report["importedClosed"] = true;
                }

                break;
            }

            case "close":
                project.Close();
                report["close"] = "closed";
                break;
            default:
                report["refused"] = "unknown mode " + p.Mode;
                break;
        }
    }

    private static void ImportDxf(string file, Page page)
    {
        var transformation = new Transformation(file, page, string.Empty, Transformation.ScalingMode.Automatic, Transformation.PositionMode.Center, false);
        new Import().DXFPage(file, page, transformation, string.Empty);
    }

    private static JObject Placements(Project project, System.Action change)
    {
        Page page = PageNamed(project, DxfPage);
        int before = page.AllPlacements.Length;
        JToken outcome = Timed(change);
        return new JObject { ["before"] = before, ["outcome"] = outcome, ["after"] = page.AllPlacements.Length };
    }

    private static JObject PlacementsRolledBack(Project project, System.Action change)
    {
        JObject result;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            result = Placements(project, change);
            safety.Rollback();
        }

        result["afterRollback"] = PageNamed(project, DxfPage).AllPlacements.Length;
        return result;
    }

    private static JObject PartsState(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        Function[] functions = finder.GetFunctions(null);
        return new JObject
        {
            ["projectArticles"] = Try(() => project.Articles.Length),
            ["articleReferences"] = functions.Sum(f => f.ArticleReferences.Length),
        };
    }

    private static JToken Timed(System.Action work)
    {
        var clock = Stopwatch.StartNew();
        var result = new JObject();
        try
        {
            work();
            result["ok"] = true;
        }
        catch (Exception ex)
        {
            result["ok"] = false;
            result["error"] = Describe(ex);
        }

        result["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2);
        return result;
    }

    private static Page NewPage(Project project, int counter)
    {
        using var properties = new PagePropertyList();
        properties.DESIGNATION_PLANT = "GAA";
        properties.DESIGNATION_LOCATION = "A1";
        properties.DESIGNATION_DOCTYPE = "EFS1";
        properties.PAGE_COUNTER = counter;
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, properties);
        return page;
    }

    private static Page PageNamed(Project project, int counter)
    {
        string name = "=GAA+A1&EFS1/" + counter.ToString(CultureInfo.InvariantCulture);
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).FirstOrDefault(pg => pg.Name == name) ?? throw new InvalidOperationException("probe page missing; run 'pages' first: " + name);
    }

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

        public string In { get; set; } = string.Empty;

        public string Restore { get; set; } = string.Empty;
    }
}
