using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;

namespace EplanMcp.Spikes.S36;

/// <summary>
/// S32-style deferred action; only one request owns the dispatcher probe at a time.
/// No EPLAN objects survive the callback. The maintainer performs undo and project lifecycle actions separately.
/// </summary>
public sealed class SpikeS36Action : IEplAction
{
    private static int _running;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        Name = "EplanMcpSpikeS36";
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
            if (!string.Equals(Path.GetExtension(log), ".jsonl", StringComparison.OrdinalIgnoreCase) || File.Exists(log))
            {
                throw new ArgumentException("Log must name a new .jsonl file.");
            }

            string project = Parameter(oActionCallingContext, "Project");
            string step = Parameter(oActionCallingContext, "Step");
            string variant = Parameter(oActionCallingContext, "Variant");
            string fail = Parameter(oActionCallingContext, "Fail");
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            var worker = new Thread(() => RunLater(dispatcher, log, project, step, variant, fail)) { IsBackground = true };
            worker.Start();
            return true;
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _running, 0);
            return false;
        }
    }

    private static void RunLater(Dispatcher dispatcher, string log, string projectPath, string step, string variant, string fail)
    {
        bool ok = false;
        void Log(string phase, object data) => File.AppendAllText(log,
            JsonConvert.SerializeObject(new { phase, step, variant, fail, utc = DateTime.UtcNow, data }) + "\n", new UTF8Encoding(false));
        try
        {
            Thread.Sleep(300); // Let the Remote Client action return before entering the dispatcher (S32).
            object? marker = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(120), new Func<bool>(() =>
            {
                try
                {
                    using var process = Process.GetCurrentProcess();
                    if (process.MainWindowHandle != IntPtr.Zero && !IsWindowEnabled(process.MainWindowHandle))
                    {
                        throw new InvalidOperationException("EPLAN has a modal dialog; close it before retrying.");
                    }

                    using var locking = new LockingStep();
                    Validate(projectPath, step, variant, fail);
                    Project project = Open();
                    Project? current = new SelectionSet().GetCurrentProject(false);
                    if (current is null || !Allowed(current.ProjectLinkFilePath))
                    {
                        throw new InvalidOperationException("Make LargeWrite the current project first.");
                    }

                    using (var probe = new Probe(project))
                    {
                        if (step == "readback")
                        {
                            Log("readback", probe.Read());
                        }
                        else
                        {
                            Log("before", probe.Read());
                            Write(probe, step, variant, fail == "true", Log);
                            Log("after", probe.Read());
                        }
                    }

                    ok = true;
                }
                catch (Exception ex)
                {
                    // Never let an exception escape a dispatcher callback (S1/A6), including a log failure.
                    TryLog(Log, "error", ex);
                }

                return true;
            }));
            if (marker is null)
            {
                Log("timeout", new { outcome = "unknown", instruction = "Do not retry a write; inspect EPLAN and the log." });
            }
        }
        catch (Exception ex)
        {
            TryLog(Log, "error", ex);
        }
        finally
        {
            try
            {
                Log("done", new { ok });
            }
            catch (Exception ex)
            {
                Trace.TraceError("S36 log failure: {0}", ex.GetType().Name);
            }

            Interlocked.Exchange(ref _running, 0);
        }
    }

    private static void Write(Probe probe, string step, string variant, bool fail, Action<string, object> log)
    {
        using var safety = SafetyPoint.Create();
        using var manager = new UndoManager();
        UndoStep undo = manager.CreateUndoStep(); // Must be inside the SafetyPoint (S2, product TransactionScope).
        undo.SetUndoDescription("S36: " + step);
        bool injected = false;
        try
        {
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                if (step == "sentinel")
                {
                    probe.Sentinel();
                }
                else
                {
#pragma warning disable CA2000 // Borrowed placement: never dispose EPLAN data-model objects (A39).
                    PlaceHolder holder = probe.Insert(log);
#pragma warning restore CA2000
                    log("before_apply", probe.Read());
                    log("apply_enter", new { quiet = "ShowNoDialogs", overload = variant });
                    var watch = Stopwatch.StartNew();
                    if (variant == "default")
                    {
                        holder.ApplyRecord("Apply");
                    }
                    else
                    {
                        holder.ApplyRecord("Apply", false);
                    }

                    log("after_apply", new { elapsedMs = watch.ElapsedMilliseconds, state = probe.Read() });
                    if (fail)
                    {
                        injected = true;
                        throw new InvalidOperationException("S36 deliberate failure after ApplyRecord.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            safety.Rollback(); // QuietMode has ended; do not CloseUndo on a rollback (product TransactionScope).
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

    private static void Validate(string project, string step, string variant, string fail)
    {
        if (!Allowed(project))
        {
            throw new InvalidOperationException("Only %USERPROFILE%/EplanMcp/scratch/writes/LargeWrite.elk is allowed.");
        }

        RejectLinks(project);
        RejectLinks(Path.ChangeExtension(project, ".edb"));
        if (step is not ("sentinel" or "apply" or "readback") || variant is not ("default" or "false") || fail is not ("true" or "false") || (fail == "true" && step != "apply"))
        {
            throw new ArgumentException("Step=sentinel|apply|readback, Variant=default|false, Fail=true|false (only apply may fail).");
        }
    }

    private static Project Open()
    {
        using var manager = new ProjectManager();
        bool previous = manager.LockProjectByDefault;
        try
        {
            manager.LockProjectByDefault = false;
            return manager.OpenProjects.Single(p => Allowed(p.ProjectLinkFilePath));
        }
        finally
        {
            manager.LockProjectByDefault = previous;
        }
    }

    private static bool Allowed(string path) => string.Equals(Path.GetFullPath(path),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch", "writes", "LargeWrite.elk"), StringComparison.OrdinalIgnoreCase);

    private static void RejectLinks(string path)
    {
        for (string? entry = Path.GetFullPath(path); entry is not null; entry = Path.GetDirectoryName(entry))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("The scratch project path must not traverse a link or junction.");
            }
        }
    }

    private static void TryLog(Action<string, object> log, string phase, Exception ex)
    {
        try
        {
            log(phase, new { error = ex.GetType().Name, message = ex.Message });
        }
        catch (Exception failure)
        {
            Trace.TraceError("S36 log failure: {0}", failure.GetType().Name);
        }
    }

    private static string Parameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        return value;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
}
