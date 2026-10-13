using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base.Internal;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S1;

/// <summary>
/// Spike S1 (dispatcher). The real Bridge receives requests on a pipe (background) thread and must run
/// EPLAN API code on the main thread. This action captures the main-thread dispatcher, then starts a
/// background thread that exercises one scenario and writes a JSON report; the action returns at once so
/// the main thread is idle, exactly as it is between requests in production.
/// <para>
/// Usage: <c>EplanMcpSpikeS1 /Mode:bg /Scenario:probe|idle|busy|exception|rollback|undoCommit|noUndo|modal
/// /Report:"…\x.json" /ScratchRoot:"…" [/Counter:n]</c>; also /Mode:op|check|cleanup run on the main thread.
/// </para>
/// </summary>
public sealed class SpikeS1Action : IEplAction
{
    private const string Location = "MCPS1";
    private const string ModalTitle = "EplanMcp S1 modal test";
    private const int BusyMilliseconds = 3000;

    private static int _mainThreadId;
    private static IntPtr _mainWindow;
    private static Dispatcher? _dispatcher;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS1Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S1";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        var p = new Params(oActionCallingContext);
        try
        {
            JObject mainSnapshot = CaptureMainThread();
            switch (p.Mode)
            {
                case "bg":
                    StartBackground(p, mainSnapshot);
                    if (p.Scenario == "busy")
                    {
                        // Keep the main thread busy while the background thread tries to dispatch.
                        Thread.Sleep(BusyMilliseconds);
                    }

                    break;
                case "op":
                    Write(p.Report, OnMainThread(p, () => Op(p, withUndo: true)));
                    break;
                case "check":
                    Write(p.Report, OnMainThread(p, () => new JObject { ["spikePages"] = SpikePageNames(CurrentProject()) }));
                    break;
                case "cleanup":
                    Write(p.Report, OnMainThread(p, () => RemoveSpikePages(CurrentProject())));
                    break;
                default:
                    Write(p.Report, new JObject { ["error"] = "unknown mode " + p.Mode });
                    break;
            }
        }
        catch (Exception ex)
        {
            Write(p.Report, new JObject { ["fatal"] = Describe(ex) });
        }

        return true;
    }

    // ---------------------------------------------------------------- main thread capture

    private static JObject CaptureMainThread()
    {
        var snapshot = new JObject
        {
            ["mainThreadId"] = Environment.CurrentManagedThreadId,
            ["apartment"] = Thread.CurrentThread.GetApartmentState().ToString(),
        };

        Dispatcher? before = EplanMainThreadDispatcher.GetMainThreadDispatcher();
        snapshot["eplanDispatcherPresetByEplan"] = before is not null;
        Dispatcher current = Dispatcher.CurrentDispatcher;
        snapshot["currentDispatcherIsEplans"] = before is not null && ReferenceEquals(before, current);
        if (before is null)
        {
            EplanMainThreadDispatcher.SetMainThreadDispatcher(current);
        }

        snapshot["canAccessMainThreadOnMain"] = EplanMainThreadDispatcher.CanAccessMainThread();

        _mainThreadId = Environment.CurrentManagedThreadId;
        _mainWindow = Process.GetCurrentProcess().MainWindowHandle;
        _dispatcher = EplanMainThreadDispatcher.GetMainThreadDispatcher() ?? current;
        return snapshot;
    }

    private static void StartBackground(Params p, JObject mainSnapshot)
    {
        var thread = new Thread(() =>
        {
            // An unhandled exception on a background thread would terminate EPLAN: catch everything.
            var report = new JObject { ["scenario"] = p.Scenario, ["main"] = mainSnapshot, ["bgThreadId"] = Environment.CurrentManagedThreadId };
            try
            {
                report["result"] = RunScenario(p);
            }
            catch (Exception ex)
            {
                report["fatal"] = Describe(ex);
            }

            Write(p.Report, report);
        })
        {
            IsBackground = true,
            Name = "EplanMcp S1 " + p.Scenario,
        };
        thread.Start();
    }

    private static JToken RunScenario(Params p) => p.Scenario switch
    {
        "probe" => Probe(),
        "idle" => IdlePriorities(),
        "busy" => Busy(),
        "exception" => ExceptionPropagation(),
        "rollback" => RollbackInDispatcher(p),
        "undoCommit" => DispatchWrite(() => Op(p, withUndo: true)),
        "noUndo" => DispatchWrite(() => Op(p, withUndo: false)),
        "modal" => Modal(),
        "modalGuard" => ModalGuard(),
        _ => new JObject { ["error"] = "unknown scenario " + p.Scenario },
    };

    // ---------------------------------------------------------------- scenarios

    private static JObject Probe()
    {
        var result = new JObject();
        using (var eplanDispatcher = new EplanMainThreadDispatcher())
        {
            result["canAccessMainThreadOnBackground"] = EplanMainThreadDispatcher.CanAccessMainThread();
            result["eplanSync"] = Measure(20, () =>
                (int)eplanDispatcher.ExecuteInMainThreadSync(_ => Environment.CurrentManagedThreadId, null));
        }

        result["wpfInvoke"] = Measure(20, () =>
            (int)_dispatcher!.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(5),
                new Func<int>(() => Environment.CurrentManagedThreadId)));

        result["readWithoutLockingStep"] = Attempt(() => Dispatch(() => new JObject { ["pages"] = CurrentProject().Pages.Length }));
        result["readWithLockingStep"] = Attempt(() => Dispatch(() =>
        {
            using var locking = new LockingStep();
            return new JObject { ["pages"] = CurrentProject().Pages.Length };
        }));
        return result;
    }

    private static JObject IdlePriorities()
    {
        var result = new JObject();
        foreach (DispatcherPriority priority in new[]
                 { DispatcherPriority.Send, DispatcherPriority.Normal, DispatcherPriority.Background, DispatcherPriority.ContextIdle, DispatcherPriority.ApplicationIdle, DispatcherPriority.SystemIdle })
        {
            var watch = Stopwatch.StartNew();
            object? value = _dispatcher!.Invoke(priority, TimeSpan.FromSeconds(5), new Func<int>(() => Environment.CurrentManagedThreadId));
            result[priority.ToString()] = new JObject
            {
                ["executed"] = value is not null,
                ["onMainThread"] = value is int id && id == _mainThreadId,
                ["ms"] = watch.ElapsedMilliseconds,
            };
        }

        return result;
    }

    private static JObject Busy()
    {
        Thread.Sleep(300); // make sure the action is inside its Thread.Sleep on the main thread
        long executedAtMs = -1;
        var clock = Stopwatch.StartNew();

        object? value = _dispatcher!.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(1), new Func<int>(() =>
        {
            Interlocked.Exchange(ref executedAtMs, clock.ElapsedMilliseconds);
            return 1;
        }));
        long invokeReturnedMs = clock.ElapsedMilliseconds;

        long eplanSyncReturnedMs;
        using (var eplanDispatcher = new EplanMainThreadDispatcher())
        {
            eplanDispatcher.ExecuteInMainThreadSync(_ => null, null);
            eplanSyncReturnedMs = clock.ElapsedMilliseconds;
        }

        Thread.Sleep(2000); // would an aborted operation still run once the main thread is free?
        return new JObject
        {
            ["wpfInvokeWithTimeout1s"] = new JObject
            {
                ["returnedAfterMs"] = invokeReturnedMs,
                ["returnedValue"] = value is null ? null : JToken.FromObject(value),
                ["delegateExecutedAtMs"] = Interlocked.Read(ref executedAtMs),
            },
            ["eplanSyncReturnedAfterMs"] = eplanSyncReturnedMs,
            ["mainThreadBusyForMs"] = BusyMilliseconds,
        };
    }

    /// <summary>
    /// First run (2026-10-13 03:18): a delegate that let an exception escape on the main thread terminated
    /// EPLAN (unhandled exception in Dispatcher.ProcessQueue). Only the safe pattern is exercised now: the
    /// delegate catches everything itself and the failure is handed back to the background caller.
    /// </summary>
    private static JObject ExceptionPropagation()
    {
        return new JObject
        {
            ["caughtInsideDelegate"] = Attempt(() => Dispatch(() => throw new InvalidOperationException("thrown on the main thread"))),
            ["dispatchStillWorksAfterwards"] = Attempt(() => Dispatch(() => new JObject { ["ok"] = true })),
        };
    }

    private static JObject RollbackInDispatcher(Params p)
    {
        int c = p.Counter;
        return new JObject
        {
            ["noLockingStep_sp_uncommitted"] = Attempt(() => Dispatch(() => CreateAbandoned(c + 1, locking: false, undo: "none"))),
            ["lockingStep_sp_uncommitted"] = Attempt(() => Dispatch(() => CreateAbandoned(c + 2, locking: true, undo: "none"))),
            ["lockingStep_sp_undoInside_uncommitted"] = Attempt(() => Dispatch(() => CreateAbandoned(c + 3, locking: true, undo: "inside"))),
            ["lockingStep_undoOutside_sp_uncommitted"] = Attempt(() => Dispatch(() => CreateAbandoned(c + 4, locking: true, undo: "outside"))),
        };
    }

    private static JObject Modal()
    {
        IntPtr owner = Process.GetCurrentProcess().MainWindowHandle;
        var clock = Stopwatch.StartNew();

        // Show a modal message box ON the main thread, owned by EPLAN's main window (as an EPLAN dialog is).
        _dispatcher!.BeginInvoke(DispatcherPriority.Normal, new System.Action(() => MessageBoxW(owner, "Spike S1: closes automatically.", ModalTitle, 0)));
        Thread.Sleep(800);
        bool boxVisible = FindWindowW(null, ModalTitle) != IntPtr.Zero;

        long opRanAtMs = -1;
        bool mainWindowEnabledDuringOp = true;
        object? value = _dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(2), new Func<int>(() =>
        {
            Interlocked.Exchange(ref opRanAtMs, clock.ElapsedMilliseconds);
            mainWindowEnabledDuringOp = IsWindowEnabled(owner);
            return Environment.CurrentManagedThreadId;
        }));
        long invokeReturnedMs = clock.ElapsedMilliseconds;

        IntPtr box = FindWindowW(null, ModalTitle);
        if (box != IntPtr.Zero)
        {
            PostMessageW(box, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        }

        long closedAtMs = clock.ElapsedMilliseconds;
        Thread.Sleep(500);
        return new JObject
        {
            ["boxVisibleBeforeOp"] = boxVisible,
            ["opExecuted"] = value is not null,
            ["opRanAtMs"] = Interlocked.Read(ref opRanAtMs),
            ["invokeReturnedAtMs"] = invokeReturnedMs,
            ["boxClosedAtMs"] = closedAtMs,
            ["opRanWhileModalOpen"] = value is not null && Interlocked.Read(ref opRanAtMs) < closedAtMs,
            ["mainWindowEnabledDuringOp"] = mainWindowEnabledDuringOp,
            ["boxStillOpenAfterClose"] = FindWindowW(null, ModalTitle) != IntPtr.Zero,
        };
    }

    private static JObject ModalGuard()
    {
        var clock = Stopwatch.StartNew();
        _dispatcher!.BeginInvoke(DispatcherPriority.Normal, new System.Action(() => MessageBoxW(_mainWindow, "Spike S1: closes automatically.", ModalTitle, 0)));
        Thread.Sleep(800);
        bool boxVisible = FindWindowW(null, ModalTitle) != IntPtr.Zero;
        JObject whileOpen = Attempt(() => GuardedDispatch(() => new JObject { ["executedAtMs"] = clock.ElapsedMilliseconds }));

        IntPtr box = FindWindowW(null, ModalTitle);
        if (box != IntPtr.Zero)
        {
            PostMessageW(box, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        }

        Thread.Sleep(500);
        JObject afterClose = Attempt(() => GuardedDispatch(() => new JObject { ["executedAtMs"] = clock.ElapsedMilliseconds }));
        return new JObject
        {
            ["boxVisible"] = boxVisible,
            ["whileModalOpen"] = whileOpen,
            ["afterModalClosed"] = afterClose,
        };
    }

    /// <summary>
    /// The production rule under test: on the main thread, before doing anything, refuse when EPLAN's main
    /// window is disabled (a modal dialog is open), because the dispatcher would otherwise run us re-entrantly
    /// inside that dialog's message loop.
    /// </summary>
    private static JToken GuardedDispatch(Func<JToken> work) => Dispatch(() =>
        IsWindowEnabled(_mainWindow)
            ? work()
            : new JObject { ["refused"] = "EPLAN_BUSY: a modal dialog is open (main window disabled)" });

    // ---------------------------------------------------------------- EPLAN operations (main thread)

    private static Project CurrentProject() =>
        new SelectionSet().GetCurrentProject(false) ?? throw new InvalidOperationException("no project open");

    private static void RequireScratch(Project project, string scratchRoot)
    {
        string root = Path.GetFullPath(scratchRoot).TrimEnd('\\') + "\\";
        if (string.IsNullOrEmpty(scratchRoot) || !Path.GetFullPath(project.ProjectFullName).StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("refused: project is outside the scratch root");
        }
    }

    private static Page NewPage(Project project, int counter)
    {
        using var properties = new PagePropertyList();
        properties.DESIGNATION_LOCATION = Location;
        properties.PAGE_COUNTER = counter;
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, properties);
        return page;
    }

    private static JObject CreateAbandoned(int counter, bool locking, string undo)
    {
        // r1 created the LockingStep after CurrentProject()/ProjectFullName and every variant failed with
        // NoLockingStepException: outside an action even those reads need it, so it must come first.
        LockingStep? lockingStep = locking ? new LockingStep() : null;
        try
        {
            Project project = CurrentProject();
            RequireScratch(project, _scratchRoot);
            string name;
            using var undoManager = new UndoManager();
            UndoStep? outer = undo == "outside" ? undoManager.CreateUndoStep() : null;
            using (SafetyPoint safetyPoint = SafetyPoint.Create())
            {
                UndoStep? inner = undo == "inside" ? undoManager.CreateUndoStep() : null;
                name = NewPage(project, counter).Name;
                inner?.CloseUndo();
            }

            outer?.CloseUndo();
            bool survived = project.Pages.Any(page => page.Name == name);
            return new JObject { ["created"] = name, ["survived"] = survived, ["rolledBack"] = !survived };
        }
        finally
        {
            lockingStep?.Dispose();
        }
    }

    /// <summary>The production write pattern: LockingStep → SafetyPoint → UndoStep (inside) → commit → close.</summary>
    private static JObject Op(Params p, bool withUndo)
    {
        using var locking = new LockingStep();
        Project project = CurrentProject();
        RequireScratch(project, p.ScratchRoot);
        using var undoManager = new UndoManager();
        string name;
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            UndoStep? undo = withUndo ? undoManager.CreateUndoStep() : null;
            undo?.SetUndoDescription("AI: spike S1 page " + p.Counter.ToString(CultureInfo.InvariantCulture));
            name = NewPage(project, p.Counter).Name;
            safetyPoint.Commit();
            undo?.CloseUndo();
        }

        return new JObject { ["created"] = name, ["withUndo"] = withUndo, ["spikePages"] = SpikePageNames(project) };
    }

    private static JArray SpikePageNames(Project project) =>
        new(project.Pages.Where(IsSpikePage).Select(page => page.Name));

    private static bool IsSpikePage(Page page) => page.Name.IndexOf("+" + Location + "/", StringComparison.Ordinal) >= 0;

    private static JObject RemoveSpikePages(Project project)
    {
        var removed = new JArray();
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            foreach (Page page in project.Pages.Where(IsSpikePage).ToArray())
            {
                removed.Add(page.Name);
                page.Remove();
            }

            safetyPoint.Commit();
        }

        return new JObject { ["removed"] = removed, ["pagesAfter"] = project.Pages.Length };
    }

    // ---------------------------------------------------------------- dispatch helpers

    private static string _scratchRoot = string.Empty;

    private static JToken OnMainThread(Params p, Func<JToken> work)
    {
        _scratchRoot = p.ScratchRoot;
        return work();
    }

    /// <summary>Runs <paramref name="work"/> on the main thread with a 10 s start timeout, as the Bridge will.</summary>
    private static JToken Dispatch(Func<JToken> work)
    {
        JToken? result = null;
        Exception? failure = null;
        object? marker = _dispatcher!.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(10), new Func<int>(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            return 1;
        }));
        if (marker is null)
        {
            throw new TimeoutException("main thread did not pick up the work within 10 s");
        }

        if (failure is not null)
        {
            throw new InvalidOperationException("work failed on the main thread", failure);
        }

        return result!;
    }

    private static JToken DispatchWrite(Func<JToken> work) => Dispatch(work);

    private static JObject Measure(int count, Func<int> call)
    {
        var timings = new List<double>();
        bool allOnMain = true;
        for (int i = 0; i < count; i++)
        {
            var watch = Stopwatch.StartNew();
            int threadId = call();
            timings.Add(watch.Elapsed.TotalMilliseconds);
            allOnMain &= threadId == _mainThreadId;
        }

        timings.Sort();
        return new JObject
        {
            ["calls"] = count,
            ["allOnMainThread"] = allOnMain,
            ["minMs"] = Math.Round(timings[0], 3),
            ["medianMs"] = Math.Round(timings[timings.Count / 2], 3),
            ["maxMs"] = Math.Round(timings[timings.Count - 1], 3),
        };
    }

    private static JObject Attempt(Func<object?> action)
    {
        try
        {
            object? value = action();
            return new JObject { ["ok"] = true, ["value"] = value is null ? null : JToken.FromObject(value) };
        }
        catch (Exception ex)
        {
            return new JObject { ["ok"] = false, ["error"] = Describe(ex) };
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

    private static void Write(string path, JToken report)
    {
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, report.ToString(Formatting.Indented));
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);

    private sealed class Params
    {
        public Params(ActionCallingContext context)
        {
            Mode = Get(context, "Mode");
            Scenario = Get(context, "Scenario");
            Report = Get(context, "Report");
            ScratchRoot = Get(context, "ScratchRoot");
            Counter = int.TryParse(Get(context, "Counter"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
            _scratchRoot = ScratchRoot;
        }

        public string Mode { get; }

        public string Scenario { get; }

        public string Report { get; }

        public string ScratchRoot { get; }

        public int Counter { get; }

        private static string Get(ActionCallingContext context, string name)
        {
            string value = string.Empty;
            context.GetParameter(name, ref value);
            return value ?? string.Empty;
        }
    }
}
