using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S43;

/// <summary>S36 deferred callback; the gate owns one request, and all native objects stay inside the callback.</summary>
public sealed class SpikeS43Action : IEplAction
{
    private static int _running;
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpSpikeS43"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }
    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) { return false; }
        try
        {
            string log = Evidence.LogPath(Parameter(oActionCallingContext, "Log"), false);
            if (!Directory.Exists(Path.GetDirectoryName(log))) { throw new DirectoryNotFoundException("The driver must prepare the report directory."); }
            string project = Parameter(oActionCallingContext, "Project"), step = Parameter(oActionCallingContext, "Step");
            string fail = Parameter(oActionCallingContext, "Fail"), group = Parameter(oActionCallingContext, "Group");
            Evidence.ProjectPath(project);
            Steps.Validate(step, group, fail);
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            new Thread(() => RunLater(dispatcher, log, project, step, fail == "true", group)) { IsBackground = true }.Start();
            return true;
        }
        catch (Exception ex) { Trace.TraceError("S43 rejected: {0}", ex); Interlocked.Exchange(ref _running, 0); return false; }
    }
    private static void RunLater(Dispatcher dispatcher, string log, string path, string step, bool fail, string group)
    {
        bool ok = false, ownsLog = false;
        var watch = Stopwatch.StartNew();
        void Log(string phase, object data) => File.AppendAllText(log, JsonConvert.SerializeObject(new { phase, step, group, fail, utc = DateTime.UtcNow, elapsedMs = watch.ElapsedMilliseconds, data }) + "\n", new UTF8Encoding(false));
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
                    try
                    {
                        using var probe = new Probe(project, Log);
                        if (step == "discover") { probe.Discover(Log); }
                        else if (step == "readback") { Log("readback", probe.Read()); }
                        else
                        {
                            // A52: discover before any write; Function.Create already failed in SafetyPoint in S9/S40.
                            if (Steps.Placement(step)) { probe.Discover(Log, inventory: false); }
                            probe.Preflight(step, group);
                            JObject before = probe.Read();
                            probe.LogSnapshot("before", before);
                            if (step is "sentinel" or "generate") { SafetyWrite(probe, step, group, fail, Log); }
                            else
                            {
                                project.LockAllObjects(); // A13/A52: one Transaction for all functions and property placements.
                                TransactionWrite(probe, step, group, fail, Log);
                            }
                            JObject after = probe.Read();
                            probe.LogSnapshot("after", after);
                            if (!fail) { probe.ValidateCommitted(step, group, Log); }
                            if (fail)
                            {
                                bool restored = JToken.DeepEquals(before, after);
                                Log("failure_rollback_check", new { restored });
                                if (!restored) { throw new InvalidOperationException("Deliberate failure rollback differs; inspect the fixture."); }
                            }
                        }
                        ok = true;
                    }
                    finally
                    {
                        JObject after = Evidence.Hashes(path);
                        Log("hashes_after", new { changes = Evidence.Changes(hashes, after), changed = hashes.Properties().ToDictionary(g => g.Name, g =>
                            ((JObject)g.Value).Properties().Select(p => p.Name).Union(((JObject)after[g.Name]!).Properties().Select(p => p.Name), StringComparer.Ordinal)
                            .Where(k => !JToken.DeepEquals(g.Value[k], after[g.Name]![k])).ToArray()) });
                        var messages = new SysMessagesCollection(); // A11: only the bookmark survived from the first collection.
                        Log("system_messages", messages.Cast<BaseException>().Skip(bookmark).Select(m => new { level = (int)m.MessageLevel, text = m.GetMessageText() }).ToArray());
                    }
                }
                catch (Exception ex) { ok = false; TryLog(Log, "error", ex); } // S1: no exception may escape this callback, including log failures.
                return true;
            }));
            if (done is null) { throw new TimeoutException("S43 dispatch timed out; inspect EPLAN and do not retry a write."); }
        }
        catch (Exception ex) { ok = false; if (ownsLog) { TryLog(Log, "error", ex); } else { Trace.TraceError("S43 could not reserve its log: {0}", ex); } }
        finally
        {
            try { if (ownsLog) { Log("done", new { ok }); } }
            catch (Exception ex) { Trace.TraceError("S43 log failure: {0}", ex); }
            Interlocked.Exchange(ref _running, 0);
        }
    }
    private static void SafetyWrite(Probe probe, string step, string group, bool fail, Action<string, object> log)
    {
        using var safety = SafetyPoint.Create();
        using var manager = new UndoManager();
        UndoStep undo = manager.CreateUndoStep();
        undo.SetUndoDescription("S43: " + step + " " + group);
        bool injected = false;
        bool committed = false;
        log("transaction_enter", new { chain = "SafetyPoint -> UndoStep -> QuietModeStep" });
        try
        {
            probe.BeginWrite();
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                if (step == "sentinel") { probe.Sentinel(log); }
                else { probe.GenerateOnly(group, log); }
                probe.LogSnapshot("inside_transaction", probe.Read());
                if (fail) { injected = true; throw new InvalidOperationException("S43 deliberate failure after readback."); }
            }
            safety.Commit();
            committed = true;
            probe.EndWrite();
        }
        catch (Exception ex)
        {
            if (!committed)
            {
                try { probe.PrepareRollback(); }
                finally { try { safety.Rollback(); } finally { probe.EndWrite(); } }
            }
            log("rolled_back", new { chain = "SafetyPoint", deliberate = injected, error = Evidence.Error(ex) });
            if (!injected) { throw; }
            return;
        }
        undo.CloseUndo();
        log("committed", new { chain = "SafetyPoint" });
    }
    private static void TransactionWrite(Probe probe, string step, string group, bool fail, Action<string, object> log)
    {
        using var manager = new UndoManager();
        UndoStep undo = manager.CreateUndoStep();
        undo.SetUndoDescription("S43: " + step + " " + group + " (Transaction)");
        var transactions = new TransactionManager();
        using Transaction transaction = transactions.CreateTransaction();
        bool injected = false;
        bool committed = false;
        log("transaction_enter", new { chain = "explicit LockAllObjects -> UndoStep -> Transaction -> QuietModeStep" });
        try
        {
            probe.BeginWrite();
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                if (step == "cores") { probe.AssignCores(group, log); }
                else if (step == "bind") { probe.BindCable(group, log); }
                else { probe.Place(step, log); }
                if (fail) { injected = true; throw new InvalidOperationException("S43 deliberate failure after readback."); }
            }
            transaction.Commit();
            committed = true;
            probe.EndWrite();
        }
        catch (Exception ex)
        {
            if (!committed)
            {
                try { probe.PrepareRollback(); }
                finally { try { transaction.Abort(); } finally { probe.EndWrite(); } }
            }
            log("rolled_back", new { chain = "Transaction.Abort", deliberate = injected, stage = probe.Stage, error = Evidence.Error(ex) });
            if (!injected) { throw; }
            return;
        }
        undo.CloseUndo();
        log("committed", new { chain = "Transaction" });
    }
    private static void TryLog(Action<string, object> log, string phase, Exception ex)
    {
        try { log(phase, Evidence.Error(ex)); }
        catch (Exception failure) { Trace.TraceError("S43 log failure: {0}", failure); }
    }
    private static string Parameter(ActionCallingContext context, string name) { string value = string.Empty; context.GetParameter(name, ref value); return value; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
}
