using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S26;

/// <summary>S34's deferred main-thread probes; the gate owns the entire mode, including publication.</summary>
public sealed partial class SpikeS26Action : IEplAction
{
    private static int _busy;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        Name = (typeof(SpikeS26Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S26").Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        bool ownsGate = false;
        try
        {
            string encoded = string.Empty;
            oActionCallingContext.GetParameter("RequestBase64", ref encoded);
            if (encoded.Length > 16384) { throw new ArgumentException("Request is too large."); }
            JObject request = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            var p = new Parameters(request);
            ownsGate = Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
            if (!ownsGate) { throw new InvalidOperationException("S26 is already running."); }
            if (File.Exists(p.Report)) { throw new IOException("A report already exists for this mode."); }
            SpikeS26Paths.Reserve(p.Folder, p.Mode, ".claimed");
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            new Thread(() => RunLater(dispatcher, p)) { IsBackground = true }.Start();
            return true;
        }
        catch (Exception ex)
        {
            if (ownsGate) { Interlocked.Exchange(ref _busy, 0); }
            // An invalid or duplicate request must never overwrite a previous run's evidence.
            Trace.WriteLine("S26 scheduling refused: " + Describe(ex).ToString(Formatting.None));
            return false;
        }
    }

    private static void RunLater(Dispatcher dispatcher, Parameters p)
    {
        var report = new JObject
        {
            ["spike"] = "S26-action-run", ["revision"] = "r1", ["mode"] = p.Mode,
            ["assembly"] = typeof(SpikeS26Action).Assembly.GetName().Name,
            ["timestampUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["outputFolder"] = p.Folder, ["project"] = p.Project,
            ["quietMode"] = "ShowNoDialogs", ["timeoutSeconds"] = SpikeS26Paths.Timeouts[p.Mode],
            ["engineerDialogObservation"] = JValue.CreateNull(),
            ["undoEntries"] = new JObject { ["unavailable"] = "2026 public UndoManager/UndoStep catalog exposes no history enumeration. No internal API or undo action is called." },
        };
        try
        {
            Thread.Sleep(300); // S34: let the scheduling action return before entering the dispatcher.
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(SpikeS26Paths.Timeouts[p.Mode]), new Func<bool>(() =>
            {
                try
                {
                    using var locking = new LockingStep(); // Must precede every EPLAN read (S1).
                    if (MainWindowDisabled()) { throw new InvalidOperationException("Close EPLAN's modal dialog before running S26."); }
                    using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                    Run(report, p);
                }
                catch (Exception ex) { report["fatal"] = Describe(ex); }
                return true; // Never let an exception escape the main-thread delegate (S1).
            }));
            if (done is null) { report["fatal"] = "Dispatcher did not start within the mode deadline."; }
        }
        catch (Exception ex) { report["fatal"] = Describe(ex); }
        finally
        {
            // Publish before releasing; a new mode must not overlap evidence publication.
            WriteReport(p.Report, report);
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static void Run(JObject report, Parameters p)
    {
        string[] paths = OpenProjectPaths(); // Refuse if ANY open project is outside scratch.
        if (!paths.Contains(p.Project, StringComparer.OrdinalIgnoreCase)) { throw new InvalidOperationException("Open the target scratch copy first."); }
        report["openProjects"] = new JArray(paths);
        report["currentProjectBefore"] = CurrentProjectPath();
        var baseline = paths.ToDictionary(path => path, ProjectFiles, StringComparer.OrdinalIgnoreCase);
        report["modeBaseline"] = Stage(paths, baseline, null);
        report["modeBaselineFiles"] = JObject.FromObject(baseline.ToDictionary(pair => pair.Key, pair => Summary(pair.Value, pair.Value, true), StringComparer.OrdinalIgnoreCase));
        var candidates = new List<Candidate>();
        using (var manager = new ProjectManager())
        {
            Project project = FindProject(manager, p.Project);
            switch (p.Mode)
            {
                case "runner": Runner(candidates, project, p, paths); break;
                case "layers": Layers(candidates, project, p); break;
                case "parts": Parts(candidates, report, p); break;
                case "plc": Plc(candidates, report, project, p); break;
                case "properties":
                    candidates.Add(Export(p, "placement-schemas", "projectmanagement", "EXPORTPROPERTYPLACEMENTSSCHEMAS", "FILENAME", "placements.xml"));
                    candidates.Add(Export(p, "user-properties", "XEsUserPropertiesExportAction", null, "XMLFile", "properties.xml", "Project"));
                    break;
                case "external": External(candidates, report, p); break;
            }
        }

        var attempts = new JArray();
        report["attempts"] = attempts;
        var clock = Stopwatch.StartNew();
        string? stop = null;
        foreach (Candidate candidate in candidates)
        {
            if (clock.Elapsed.TotalSeconds >= SpikeS26Paths.Timeouts[p.Mode] - 20) { stop ??= "Mode deadline reached; no further candidate was started."; }
            candidate.Skip ??= stop;
            JObject result = Probe(candidate, p, paths, baseline);
            attempts.Add(result);
            // Persist completed attempts even if a later native action blocks or crashes EPLAN.
            WriteReport(p.Report + ".progress.json", report);
            if (result.Value<bool?>("unsafeToContinue") == true) { stop = "Previous candidate left a dialog, lost an open project, or failed rollback/scope cleanup; inspect EPLAN."; }
        }

        if (stop is not null) { report["stopReason"] = stop; }
        report["afterAllAttempts"] = Stage(paths, baseline, null);
        report["currentProjectAfter"] = CurrentProjectPath();
    }

    private static JObject Probe(Candidate c, Parameters p, string[] paths, Dictionary<string, FileSnapshot> baseline)
    {
        var result = new JObject
        {
            ["id"] = c.Id, ["action"] = c.Action, ["parameters"] = JObject.FromObject(c.Options),
            ["outputFolder"] = c.Folder, ["returned"] = JValue.CreateNull(), ["elapsedMs"] = JValue.CreateNull(),
            ["dialogAppeared"] = JValue.CreateNull(), ["engineerDialogObservation"] = JValue.CreateNull(),
            ["undoWrapped"] = c.WithUndo, ["note"] = c.Note,
        };
        if (c.Skip is not null) { result["notRun"] = c.Skip; return result; }
        int bookmark = -1;
        try
        {
            OpenProjectPaths(); // Revalidate immediately before every candidate.
            if (MainWindowDisabled()) { throw new InvalidOperationException("Main window is disabled; refusing candidate."); }
            if (c.CurrentOnly && !string.Equals(CurrentProjectPath(), p.Project, StringComparison.OrdinalIgnoreCase))
            {
                result["notRun"] = "Select only the target scratch project in EPLAN first; the spike never changes selection.";
                return result;
            }

            if (Directory.Exists(c.Folder)) { throw new IOException("Candidate folder already exists."); }
            Directory.CreateDirectory(c.Folder);
            if (c.ExpectedRelative?.Contains('/') == true) { Directory.CreateDirectory(Path.Combine(c.Folder, "slash 子目录")); }
            Capture(result, "currentProjectBefore", () => new JValue(CurrentProjectPath()));
            Capture(result, "before", () => Stage(paths, baseline, c.LayerName, c.PartsDatabase));
            // A11/S34: retain only the integer bookmark, never an old collection/message.
            Capture(result, "systemMessageBookmark", () => { bookmark = new SysMessagesCollection().Count; return new JValue(bookmark); });
            try
            {
                using (SafetyPoint safety = SafetyPoint.Create())
                {
                    try
                    {
                        if (c.WithUndo)
                        {
                            using var undoManager = new UndoManager();
                            using UndoStep undo = undoManager.CreateUndoStep();
                            undo.SetUndoDescription("S26 " + c.Id);
                            try { ExecuteBuiltIn(result, c); }
                            finally
                            {
                                Capture(result, "after", () => Stage(paths, baseline, c.LayerName, c.PartsDatabase));
                                try { undo.CloseUndo(); result["closeUndoReturned"] = true; }
                                catch (Exception ex) { result["closeUndoException"] = Describe(ex); }
                            }
                        }
                        else
                        {
                            try { ExecuteBuiltIn(result, c); }
                            finally { Capture(result, "after", () => Stage(paths, baseline, c.LayerName, c.PartsDatabase)); }
                        }
                    }
                    catch (Exception ex) { result["exception"] = Describe(ex); }
                    finally
                    {
                        Capture(result, "mainWindowDisabledImmediatelyAfterAction", () => new JValue(MainWindowDisabled()));
                        Capture(result, "afterUndoStepDisposed", () => Stage(paths, baseline, c.LayerName, c.PartsDatabase));
                        var rollbackClock = Stopwatch.StartNew();
                        try { safety.Rollback(); result["rollbackReturned"] = true; }
                        catch (Exception ex) { result["rollbackException"] = Describe(ex); }
                        finally
                        {
                            result["rollbackMs"] = rollbackClock.Elapsed.TotalMilliseconds;
                            Capture(result, "afterRollback", () => Stage(paths, baseline, c.LayerName, c.PartsDatabase));
                        }
                    }
                }
            }
            catch (Exception ex) { result["scopeException"] = Describe(ex); }
        }
        catch (Exception ex) { result["setupException"] = Describe(ex); }
        finally
        {
            Capture(result, "afterSafetyPointDisposed", () => Stage(paths, baseline, c.LayerName, c.PartsDatabase));
            Capture(result, "files", () => OutputFiles(c.Folder));
            if (bookmark >= 0) { Capture(result, "systemMessages", () => MessagesSince(bookmark)); }
            Capture(result, "mainWindowDisabledAfter", () => new JValue(MainWindowDisabled()));
            Capture(result, "currentProjectAfter", () => new JValue(CurrentProjectPath()));
            Capture(result, "fingerprintAssessment", () => Assess(result, paths, c));
            if (c.ExpectedRelative is not null) { Capture(result, "fileNameCheck", () => FileNameCheck(c)); }
            try
            {
                string[] now = OpenProjectPaths();
                result["unsafeToContinue"] = result.Value<bool?>("mainWindowDisabledAfter") != false ||
                    result.Value<bool?>("mainWindowDisabledImmediatelyAfterAction") == true ||
                    result["scopeException"] is not null || result["closeUndoException"] is not null ||
                    (bookmark >= 0 && result.Value<bool?>("rollbackReturned") != true) ||
                    !new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase).SetEquals(now) ||
                    (result["fingerprintAssessment"]?["afterSafetyPointDisposed"] as JArray)?.OfType<JObject>()
                        .Any(row => row.Value<bool?>("countsChanged") == true || row.Value<bool?>("layerChanged") == true) == true ||
                    result["fingerprintAssessment"]?["partsDatabase"]?["afterSafetyPointDisposed"]?.Value<bool?>("configurationChanged") == true;
            }
            catch (Exception ex) { result["safetyCheckException"] = Describe(ex); result["unsafeToContinue"] = true; }
        }

        return result;
    }

    private static void ExecuteBuiltIn(JObject result, Candidate c)
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs); // Inside UndoStep for the Bridge-order probe.
        using var context = new ActionCallingContext();
        var echoed = new JObject();
        foreach (KeyValuePair<string, string> parameter in c.Options)
        {
            context.AddParameter(parameter.Key, parameter.Value);
            string value = string.Empty;
            context.GetParameter(parameter.Key, ref value);
            echoed[parameter.Key] = new JObject { ["value"] = value, ["exact"] = string.Equals(value, parameter.Value, StringComparison.Ordinal) };
        }

        result["contextReadBack"] = echoed; // Transport evidence, not proof of the built-in's interpretation.
        if (MainWindowDisabled()) { throw new InvalidOperationException("Main window became disabled; action refused."); }
        var action = new ActionManager().FindAction(c.Action) ?? throw new InvalidOperationException("Action not found: " + c.Action);
        using var watch = new WindowWatch();
        var clock = Stopwatch.StartNew();
        try { result["returned"] = action.Execute(context); }
        finally
        {
            result["elapsedMs"] = clock.Elapsed.TotalMilliseconds;
            result["windowObservation"] = watch.Snapshot();
        }
    }

    private static string[] OpenProjectPaths()
    {
        using var manager = new ProjectManager();
        return manager.OpenProjects.Select(project => SpikeS26Paths.Project(project.ProjectLinkFilePath)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Project FindProject(ProjectManager manager, string path) => manager.OpenProjects.FirstOrDefault(project =>
        string.Equals(project.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Project is no longer open.");

    private static string? CurrentProjectPath()
    {
        // false is essential: true can open a project-selection dialog (installed HEServices XML).
        Project? project = new SelectionSet().GetCurrentProject(false);
        return project is null ? null : SpikeS26Paths.Project(project.ProjectLinkFilePath);
    }

    private sealed class Parameters
    {
        internal Parameters(JObject value)
        {
            Mode = SpikeS26Paths.Mode(value.Value<string>("mode") ?? "");
            Folder = SpikeS26Paths.RunFolder(value.Value<string>("folder") ?? "");
            Project = SpikeS26Paths.Project(value.Value<string>("project") ?? "");
            if (!Directory.Exists(Folder)) { throw new DirectoryNotFoundException("The driver must create the new run folder first."); }
            CommonSchemeNode = value.Value<string>("commonSchemeNode") ?? "";
            ArticleSchemeNode = value.Value<string>("articleSchemeNode") ?? "";
        }

        internal string Mode { get; }
        internal string Folder { get; }
        internal string Project { get; }
        internal string Report => SpikeS26Paths.Report(Folder, Mode);
        internal string CommonSchemeNode { get; }
        internal string ArticleSchemeNode { get; }
    }

    private sealed class Candidate(string id, string action, string folder, Dictionary<string, string> options)
    {
        internal string Id { get; } = id;
        internal string Action { get; } = action;
        internal string Folder { get; } = folder;
        internal Dictionary<string, string> Options { get; } = options;
        internal string? Skip { get; set; }
        internal string? Note { get; set; }
        internal string? LayerName { get; set; }
        internal string? ExpectedRelative { get; set; }
        internal bool CurrentOnly { get; set; }
        internal bool PartsDatabase { get; set; }
        internal bool WithUndo { get; set; } = true;
    }
}
