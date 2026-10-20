using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S8;

/// <summary>
/// Spike S8 (ADR-0010, write interruption). A deliberately slow write in the Bridge's wrapping (SafetyPoint, the
/// UndoStep inside it, QuietModeStep) on EPLAN's main thread through the dispatcher, after the scheduling action has
/// returned, as S10 did: it creates pages =GAA+A1&amp;EFS1/80 onwards one by one, writing a marker file after each page
/// and after the commit, so that the driver can end EPLAN at a known point (before or after the commit).
/// <para>
/// Usage: <c>EplanMcpSpikeS8 /Mode:slowwrite|count|violation|projects|close /Report:"…\x.json" /ScratchRoot:"…\s8"
/// [/First:80] [/Count:20] [/DelayMs:500] [/HoldMs:15000]</c>. Works only on an open project under ScratchRoot, found by path.
/// </para>
/// </summary>
public sealed class SpikeS8Action : IEplAction
{
    private const int FirstCounter = 80;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS8Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S8";
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
            Count = Number(oActionCallingContext, "Count", 20),
            DelayMs = Number(oActionCallingContext, "DelayMs", 500),
            HoldMs = Number(oActionCallingContext, "HoldMs", 15000),
            First = Number(oActionCallingContext, "First", FirstCounter),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S8-write-interruption", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
        try
        {
            Thread.Sleep(300); // let the scheduling action return first
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(300), new Func<bool>(() =>
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
                report["fatal"] = "the dispatcher did not run the probes within 300 s";
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
        report["open"] = new JArray(open.Select(o => new JObject { ["name"] = o.ProjectName, ["underScratchRoot"] = IsUnder(o.ProjectFullName, p.ScratchRoot) }));
        if (p.Mode == "projects")
        {
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
            case "slowwrite":
                SlowWrite(report, project, p);
                break;
            case "violation":
                Violation(report, project, p);
                break;
            case "count":
                report["probePages"] = new JArray(ProbePages(project));
                report["pages"] = Pages(project).Length;
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

    /// <summary>
    /// U2/U3: pages one by one inside the transaction, a marker line after each and after the commit, then the main
    /// thread is held for HoldMs so that the driver can end EPLAN after the commit but before the action's end.
    /// </summary>
    private static void SlowWrite(JObject report, Project project, Params p)
    {
        string marker = p.Report + ".marker";
        File.WriteAllText(marker, "started\n");
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            using var undoManager = new UndoManager();
            UndoStep undo = undoManager.CreateUndoStep();
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                for (int i = 0; i < p.Count; i++)
                {
                    NewPage(project, p.First + i);
                    File.AppendAllText(marker, "created " + (i + 1).ToString(CultureInfo.InvariantCulture) + "\n");
                    Thread.Sleep(p.DelayMs);
                }
            }

            safety.Commit();
            undo.SetUndoDescription("S8 slow write");
            undo.CloseUndo();
        }

        File.AppendAllText(marker, "committed\n");
        report["committed"] = p.Count;
        Thread.Sleep(p.HoldMs);
        File.AppendAllText(marker, "held\n");
        report["probePages"] = new JArray(ProbePages(project));
    }

    /// <summary>
    /// U4: the native access violation of api-notes A11 (reading a message of one SysMessagesCollection after another
    /// was created) while uncommitted pages are in an open SafetyPoint.
    /// </summary>
    private static void Violation(JObject report, Project project, Params p)
    {
        string marker = p.Report + ".marker";
        File.WriteAllText(marker, "started\n");
        using SafetyPoint safety = SafetyPoint.Create();
        for (int i = 0; i < 3; i++)
        {
            NewPage(project, p.First + i);
        }

        File.AppendAllText(marker, "created 3\n");
        try
        {
            // The S6 r7 sequence: a second collection, then the first one's texts (api-notes A11).
            var first = new SysMessagesCollection();
            report["secondCount"] = new SysMessagesCollection(0, MessageLevel.Warning).Count;
            report["violationRead"] = new JArray(first.Cast<object>().OfType<BaseException>().Reverse().Take(3).Select(e => e.GetMessageText()));
        }
        catch (Exception ex)
        {
            report["violation"] = Describe(ex);
        }

        File.AppendAllText(marker, "after violation\n");
        report["probePagesBeforeRollback"] = new JArray(ProbePages(project));
        safety.Rollback();
        report["probePagesAfterRollback"] = new JArray(ProbePages(project));
    }

    private static void NewPage(Project project, int counter)
    {
        using var properties = new PagePropertyList();
        properties.DESIGNATION_PLANT = "GAA";
        properties.DESIGNATION_LOCATION = "A1";
        properties.DESIGNATION_DOCTYPE = "EFS1";
        properties.PAGE_COUNTER = counter;
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, properties);
    }

    private static Page[] Pages(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null);
    }

    private static IEnumerable<string> ProbePages(Project project) =>
        Pages(project).Select(page => page.Name).Where(name => name.StartsWith("=GAA+A1&EFS1/", StringComparison.Ordinal)
            && int.TryParse(name.Substring("=GAA+A1&EFS1/".Length), out int n) && n >= FirstCounter && n < FirstCounter + 200);

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

    private static int Number(ActionCallingContext context, string name, int fallback) =>
        int.TryParse(Parameter(context, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private sealed class Params
    {
        public string Report { get; set; } = string.Empty;

        public string ScratchRoot { get; set; } = string.Empty;

        public string Mode { get; set; } = string.Empty;

        public int Count { get; set; }

        public int DelayMs { get; set; }

        public int HoldMs { get; set; }

        public int First { get; set; }
    }
}
