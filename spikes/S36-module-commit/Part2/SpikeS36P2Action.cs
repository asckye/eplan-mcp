using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S36.Part2;

/// <summary>S36 deferred callback; the gate owns one request, and all native objects stay inside the callback.</summary>
public sealed class SpikeS36P2Action : IEplAction
{
    private static int _running;
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpSpikeS36P2"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }
    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) { return false; }
        try
        {
            string log = Evidence.LogPath(Parameter(oActionCallingContext, "Log"), false);
            if (!Directory.Exists(Path.GetDirectoryName(log))) { throw new DirectoryNotFoundException("The driver must prepare the report directory."); }
            string project = Parameter(oActionCallingContext, "Project"), step = Parameter(oActionCallingContext, "Step");
            string fail = Parameter(oActionCallingContext, "Fail");
            Evidence.ProjectPath(project);
            Steps.Validate(step, fail);
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            new Thread(() => RunLater(dispatcher, log, project, step, fail == "true")) { IsBackground = true }.Start();
            return true;
        }
        catch (Exception ex) { Trace.TraceError("S36P2 rejected: {0}", ex); Interlocked.Exchange(ref _running, 0); return false; }
    }
    private static void RunLater(Dispatcher dispatcher, string log, string path, string step, bool fail)
    {
        bool ok = false, ownsLog = false;
        string outcome = "not_started";
        var watch = Stopwatch.StartNew();
        void Log(string phase, object data) => File.AppendAllText(log, JsonConvert.SerializeObject(new { phase, step, fail, utc = DateTime.UtcNow, elapsedMs = watch.ElapsedMilliseconds, data }) + "\n", new UTF8Encoding(false));
        try
        {
            // Reserve the log before scheduling; a second request cannot reuse a pending output file.
            using (new FileStream(log, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            ownsLog = true;
            Thread.Sleep(300); // S36: let the Remote Client action return before dispatching.
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(120), new Func<bool>(() =>
            {
                try
                {
                    using var locking = new LockingStep(); // A9: first EPLAN operation, including read-only requests.
                    using var process = Process.GetCurrentProcess();
                    if (process.MainWindowHandle != IntPtr.Zero && !IsWindowEnabled(process.MainWindowHandle)) { throw new InvalidOperationException("Close the EPLAN modal dialog first."); }
                    Log("environment", new { eplanVersion = FileVersionInfo.GetVersionInfo(typeof(Project).Assembly.Location).FileVersion, processId = process.Id, modalObserved = false });
                    Evidence.ProjectPath(path);
                    using var manager = new ProjectManager();
                    bool previous = manager.LockProjectByDefault;
                    Project[] opened;
                    try { manager.LockProjectByDefault = false; opened = manager.OpenProjects; }
                    finally { manager.LockProjectByDefault = previous; }
                    // The single fixed target also prevents undo from reaching another project (A41/A46).
                    if (opened.Length != 1) { throw new InvalidOperationException("Leave only LargeWrite open and make it current."); }
                    Project project = opened.Single();
                    Evidence.ProjectPath(project.ProjectLinkFilePath);
                    Project? current = new SelectionSet().GetCurrentProject(false);
                    if (current is null) { throw new InvalidOperationException("Make LargeWrite current."); }
                    Evidence.ProjectPath(current.ProjectLinkFilePath);
                    int bookmark = new SysMessagesCollection().Count;
                    JObject hashes = Evidence.Hashes(path);
                    Log("hashes_before", hashes);
                    using var probe = new Probe(project, Log);
                    try
                    {
                        probe.Discover(); // A52: all discovery occurs before SafetyPoint.
                        if (step == "readback")
                        {
                            using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                            probe.SetStage("readback");
                            Log("readback", probe.Read());
                            Log("files_at_readback", new { files = Evidence.Files(), targetPagePresent = probe.HasWriteback });
                            if (probe.HasWriteback) { probe.ValidateWriteback(); }
                        }
                        else if (step == "closure")
                        {
                            using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                            probe.SetStage("closure");
                            Log("closure", probe.Closure());
                        }
                        else
                        {
                            JObject before;
                            using (new QuietModeStep(QuietModes.ShowNoDialogs))
                            {
                                probe.Preflight(step);
                                before = probe.Read();
                                Log("before", before);
                                Log("files_before", Evidence.Files());
                                if (step == "mutate") { probe.PrepareMutation(); }
                                probe.SetStage("write.prepare_cache");
                                probe.BeginWrite(); // Read-only baseline discovery also precedes SafetyPoint.
                            }
                            outcome = "failed";
                            bool committed = SafetyWrite(probe, step, fail, Log);
                            outcome = committed ? "committed" : "rolled_back";
                            using var readQuiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                            probe.SetStage("after_write.readback");
                            JObject after = probe.Read(); // Fresh page/finder queries; no deleted objects survive rollback.
                            Log("after", after);
                            Log("files_after", Evidence.Files());
                            if (fail || step == "mutate")
                            {
                                bool restored = JToken.DeepEquals(before, after);
                                Log("failure_rollback_check", new { restored });
                                if (!restored) { throw new InvalidOperationException("Rollback differs from the baseline."); }
                            }
                            if (step == "writeback" && !fail) { probe.ValidateWriteback(); }
                        }
                        ok = true;
                        if (outcome == "not_started") { outcome = "completed"; }
                    }
                    catch (Exception ex)
                    {
                        if (probe.OutcomeUnknown || ex is WriteRecovery.OutcomeUnknownException) { outcome = "unknown"; }
                        WriteRecovery.Log(Log, "operation_error", new { stage = probe.Stage, outcome, error = Evidence.Error(ex) });
                        throw;
                    }
                    finally
                    {
                        if (probe.OutcomeUnknown)
                        {
                            Log("diagnostics_skipped", new { outcome = "unknown", reason = "Do not touch EPLAN after failed recovery." });
                        }
                        else
                        {
                            JObject after = Evidence.Hashes(path);
                            Log("hashes_after", new { changes = Evidence.Changes(hashes, after), changed = hashes.Properties().ToDictionary(g => g.Name, g =>
                                ((JObject)g.Value).Properties().Select(p => p.Name).Union(((JObject)after[g.Name]!).Properties().Select(p => p.Name), StringComparer.Ordinal)
                                .Where(k => !JToken.DeepEquals(g.Value[k], after[g.Name]![k])).ToArray()) });
                            var messages = new SysMessagesCollection(); // A11: only the bookmark survived from the first collection.
                            Log("system_messages", messages.Cast<BaseException>().Skip(bookmark).Select(m => new { level = (int)m.MessageLevel, text = m.GetMessageText() }).ToArray());
                        }
                    }
                }
                catch (Exception ex) { ok = false; TryLog(Log, "error", ex); } // S1: no exception may escape this callback, including log failures.
                return true;
            }));
            if (done is null) { throw new TimeoutException("S36P2 dispatch timed out; inspect EPLAN and do not retry a write."); }
        }
        catch (Exception ex) { ok = false; if (ownsLog) { TryLog(Log, "error", ex); } else { Trace.TraceError("S36P2 could not reserve its log: {0}", ex); } }
        finally
        {
            try { if (ownsLog) { Log("done", new { ok, outcome }); } }
            catch (Exception ex) { Trace.TraceError("S36P2 log failure: {0}", ex); }
            Interlocked.Exchange(ref _running, 0);
        }
    }
    private static bool SafetyWrite(Probe probe, string step, bool fail, Action<string, object> log)
    {
        SafetyPoint? safety = null;
        UndoManager? manager = null;
        UndoStep? undo = null;
        bool committed = false;
        Exception? original = null;
        Exception? pending = null;
        try
        {
            probe.SetStage("safety.create");
            safety = SafetyPoint.Create();
            probe.SetStage("undo.create");
            manager = new UndoManager();
            undo = manager.CreateUndoStep();
            undo.SetUndoDescription("S36P2: " + step);
            log("transaction_enter", new { chain = "SafetyPoint -> UndoStep -> QuietModeStep" });
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                probe.SetStage(step + ".write");
                if (step == "sentinel") { probe.Sentinel(); }
                else if (step == "insert") { probe.Insert(); }
                else if (step == "mutate") { probe.Mutate(fail); }
                else { probe.Writeback(); }
                probe.SetStage(step + ".readback");
                log("inside_transaction", probe.Read());
                if (fail)
                {
                    probe.SetStage(step + ".failure.inject");
                    throw new Probe.InjectedFailure("S36P2 deliberate failure after verified writeback and ApplyRecord.");
                }
            }
            probe.SetStage("safety.commit");
            safety.Commit();
            committed = true;
            probe.EndWrite();
            probe.SetStage("undo.close");
            undo.CloseUndo();
            log("committed", new { filesOutsideTransaction = Evidence.Files(probe.OutputFolder) });
        }
        catch (Exception ex)
        {
            original = ex;
            string stage = probe.Stage;
            try
            {
                if (!committed && safety is not null)
                {
                    WriteRecovery.Rollback(ex, stage, probe.PrepareRollback, safety.Rollback, probe.EndWrite, probe.MarkOutcomeUnknown, log);
                    log("rolled_back", new { stage, deliberate = ex is Probe.InjectedFailure, staleRefusal = ex is Probe.StaleRefusal,
                        filesOutsideTransaction = Evidence.Files(probe.OutputFolder), error = Evidence.Error(ex) });
                    if (ex is not (Probe.InjectedFailure or Probe.StaleRefusal)) { pending = ex; }
                }
                else
                {
                    WriteRecovery.Log(log, "write_error", new { stage, committed, error = Evidence.Error(ex) });
                    pending = ex;
                    if (committed)
                    {
                        probe.MarkOutcomeUnknown();
                        pending = new WriteRecovery.OutcomeUnknownException(ex, new InvalidOperationException("Commit succeeded but finalization failed; do not roll back a committed scope."));
                    }
                }
            }
            catch (Exception recovery)
            {
                pending = recovery is WriteRecovery.OutcomeUnknownException ? recovery : new AggregateException(ex, recovery);
            }
        }
        finally
        {
            try
            {
                if (probe.OutcomeUnknown)
                {
                    // Never retry Rollback implicitly via Dispose, or release native handles in unknown state.
                    WriteRecovery.Quarantine(undo, manager, safety);
                }
                else
                {
                    // Preserve part 1's manager -> safety disposal order; stop at the first native cleanup failure.
                    foreach (IDisposable? handle in new IDisposable?[] { manager, safety })
                    {
                        try { handle?.Dispose(); }
                        catch (Exception ex)
                        {
                            probe.MarkOutcomeUnknown();
                            WriteRecovery.Quarantine(undo, manager, safety);
                            WriteRecovery.Log(log, "write_dispose_failed", new { stage = probe.Stage, outcome = "unknown", error = Evidence.Error(ex) });
                            pending = new WriteRecovery.OutcomeUnknownException(original ?? pending ?? ex, ex);
                            break;
                        }
                    }
                }
            }
            finally
            {
                // UndoStep has a native finalizer. Keep every scope handle rooted THROUGH recovery, including exception paths.
                GC.KeepAlive(undo);
                GC.KeepAlive(manager);
                GC.KeepAlive(safety);
            }
        }
        if (pending is not null) { ExceptionDispatchInfo.Capture(pending).Throw(); }
        return committed;
    }
    private static void TryLog(Action<string, object> log, string phase, Exception ex)
    {
        try { log(phase, Evidence.Error(ex)); }
        catch (Exception failure) { Trace.TraceError("S36P2 log failure: {0}", failure); }
    }
    private static string Parameter(ActionCallingContext context, string name) { string value = string.Empty; context.GetParameter(name, ref value); return value; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
}
