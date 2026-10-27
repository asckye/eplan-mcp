using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S44;

/// <summary>The gate owns one deferred invocation. No EPLAN object leaves the main-thread callback.</summary>
public sealed class SpikeS44Action : IEplAction
{
    private static int _running;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        Name = "EplanMcpSpikeS44";
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            string log = Parameter(oActionCallingContext, "Log");
            Paths.RunFile(log);
            if (Path.GetFileName(log) != "events.jsonl" || !Directory.Exists(Path.GetDirectoryName(log)))
            {
                throw new ArgumentException("Log must be a new events.jsonl inside an existing run folder.");
            }

            using (new FileStream(log, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
            }

            string project = Parameter(oActionCallingContext, "Project");
            string step = Parameter(oActionCallingContext, "Step");
            string fail = Parameter(oActionCallingContext, "Fail");
            string source = Parameter(oActionCallingContext, "Source");
            string pdf = Parameter(oActionCallingContext, "Pdf");
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            var worker = new Thread(() => RunLater(dispatcher, log, project, step, fail, source, pdf)) { IsBackground = true };
            worker.Start();
            return true;
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _running, 0);
            return false;
        }
    }

    private static void RunLater(Dispatcher dispatcher, string log, string projectPath, string step, string fail, string source, string pdf)
    {
        bool ok = false;
        void Log(string phase, object data) => File.AppendAllText(log,
            JsonConvert.SerializeObject(new { phase, step, fail, utc = DateTime.UtcNow, data }) + "\n", new UTF8Encoding(false));
        try
        {
            Thread.Sleep(300); // S36: let the Remote Client action return before entering the dispatcher.
            object? marker = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(120), new Func<bool>(() =>
            {
                try
                {
                    CheckWindow();
                    using var locking = new LockingStep(); // First EPLAN operation, including all project reads (S1).
                    Paths.ProjectFiles(projectPath);
                    if (step is not ("sentinel" or "import" or "readback") || fail is not ("true" or "false") || (fail == "true" && step != "import"))
                    {
                        throw new ArgumentException("Step=sentinel|import|readback, Fail=true|false (only import may fail).");
                    }

                    if (step == "import")
                    {
                        Paths.Input(source);
                        Paths.RunFile(pdf);
                        if (!string.Equals(pdf, Path.Combine(Path.GetDirectoryName(log)!, "input.pdf"), StringComparison.OrdinalIgnoreCase) ||
                            !File.Exists(pdf) || !string.Equals(Paths.Hash(source), Paths.Hash(pdf), StringComparison.Ordinal))
                        {
                            throw new ArgumentException("Import requires an unchanged input.pdf copy in this run folder.");
                        }

                        Log("input", new { source, copy = pdf, sha256 = Paths.Hash(pdf), bytes = new FileInfo(pdf).Length });
                    }
                    else if (source.Length != 0 || pdf.Length != 0)
                    {
                        throw new ArgumentException("Only import accepts a PDF.");
                    }

                    Project project = Open();
                    using var probe = new Probe(project);
                    if (step == "readback")
                    {
                        Log("readback", Read(probe));
                    }
                    else
                    {
                        Log("before", Read(probe));
                        Write(probe, step, pdf, fail == "true", Log);
                        Log("after", Read(probe));
                    }

                    ok = true;
                }
                catch (Exception ex)
                {
                    ok = false;
                    TryLog(Log, ex); // S1: even logging failures must not escape the dispatcher callback.
                }

                return true;
            }));
            if (marker is null)
            {
                Log("timeout", new { outcome = "unknown", instruction = "Inspect EPLAN and the log; do not retry a write automatically." });
            }
        }
        catch (Exception ex)
        {
            ok = false;
            TryLog(Log, ex);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0); // Release before publishing completion; a following run may start immediately.
            try
            {
                Log("done", new { ok });
            }
            catch (Exception ex)
            {
                Trace.TraceError("S44 log failure: {0}", ex.GetType().Name);
            }
        }
    }

    private static JObject Read(Probe probe)
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        return probe.Read();
    }

    private static void Write(Probe probe, string step, string pdf, bool fail, Action<string, object> log)
    {
        using var safety = SafetyPoint.Create();
        using var manager = new UndoManager();
        UndoStep undo = manager.CreateUndoStep(); // Inside SafetyPoint, matching S36 and the product TransactionScope.
        undo.SetUndoDescription("S44: " + step);
        bool injected = false;
        try
        {
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                if (step == "sentinel")
                {
                    probe.Sentinel();
                    log("after_sentinel", probe.Read());
                }
                else
                {
                    CheckWindow();
                    using var context = new ActionCallingContext();
                    context.AddParameter("TYPE", "PDFCOMMENTS");
                    context.AddParameter("PROJECTNAME", Paths.Project);
                    context.AddParameter("IMPORTFILE", pdf);
                    var action = new ActionManager().FindAction("import"); // Exactly S34; no command-line concatenation.
                    log("import_enter", new { action = "import", type = "PDFCOMMENTS", project = Paths.Project, importFile = pdf });
                    var watch = Stopwatch.StartNew();
                    bool returned = action.Execute(context);
                    long elapsedMs = watch.ElapsedMilliseconds;
                    log("after_import", new { returned, elapsedMs, state = probe.Read() });
                    if (!returned)
                    {
                        throw new InvalidOperationException("PDFCOMMENTS returned false; readback logged, import will roll back.");
                    }

                    if (fail)
                    {
                        injected = true;
                        throw new InvalidOperationException("S44 deliberate failure after import and complete readback.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            safety.Rollback(); // QuietMode has ended. Never CloseUndo on rollback (S36/A51).
            log("rolled_back", new { deliberate = injected, error = ex.GetType().Name, message = ex.Message });
            if (!injected)
            {
                throw;
            }

            return;
        }

        log("commit_enter", new { step });
        safety.Commit();
        undo.CloseUndo();
        log("committed", new { step });
    }

    private static Project Open()
    {
        using var manager = new ProjectManager();
        bool previous = manager.LockProjectByDefault;
        try
        {
            manager.LockProjectByDefault = false;
            Project[] projects = manager.OpenProjects;
            if (projects.Length != 1 || !Paths.IsProject(projects[0].ProjectLinkFilePath))
            {
                throw new InvalidOperationException("LargeWrite must be the only open project.");
            }

            Project? current = new SelectionSet().GetCurrentProject(false);
            if (current is null || !Paths.IsProject(current.ProjectLinkFilePath))
            {
                throw new InvalidOperationException("Make LargeWrite the current project first.");
            }

            return projects[0];
        }
        finally
        {
            manager.LockProjectByDefault = previous;
        }
    }

    private static void TryLog(Action<string, object> log, Exception ex)
    {
        try
        {
            log("error", new { error = ex.GetType().Name, message = ex.Message });
        }
        catch (Exception failure)
        {
            Trace.TraceError("S44 log failure: {0}", failure.GetType().Name);
        }
    }

    private static string Parameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        return value;
    }

    private static void CheckWindow()
    {
        using var process = Process.GetCurrentProcess();
        if (process.MainWindowHandle != IntPtr.Zero && !IsWindowEnabled(process.MainWindowHandle))
        {
            throw new InvalidOperationException("EPLAN has a modal dialog; close it before retrying.");
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
}
