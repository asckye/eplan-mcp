// Ported from TIA Portal Openness MCP, tests/Studio/TiaOpenness.Gui.Tests/WpfContext.cs at commit c89b7c20.
// Copyright (c) 2026 bulaofen0036-coder. MIT License, see THIRD-PARTY-NOTICES.md.
using System.Windows;
using System.Windows.Threading;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Tests;

/// <summary>
/// One STA thread with a running dispatcher, shared by every test that touches WPF: WPF objects need an STA thread, and
/// <see cref="Application"/> is one per process. The real <see cref="App"/> is created, without its startup, so that the
/// tests see the resources the product loads (App.xaml's dictionaries), not a copy that would drift.
/// </summary>
public sealed class WpfContext : IDisposable
{
    // Another run's WPF tests take seconds; one that hangs ends this run's wait.
    private static readonly TimeSpan _desktopWait = TimeSpan.FromMinutes(5);

    private readonly Thread _thread;
    private Dispatcher? _dispatcher;

    public WpfContext()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            // The desktop is one per session: another test run's windows (from another worktree, say) would take the
            // foreground, and with it the keyboard focus the tests check, from this run's. The runs take turns.
            using var desktop = new Mutex(initiallyOwned: false, @"Local\EplanMcp.Workbench.Tests.Desktop");
            if (!Take(desktop))
            {
                ready.Set();
                return;
            }

            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                if (Application.Current is null)
                {
                    CreateApp(_dispatcher);
                }

                ready.Set();
                Dispatcher.Run();
            }
            finally
            {
                desktop.ReleaseMutex();
            }
        })
        {
            IsBackground = true,
            Name = "wpf-test-ui",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        // Loading the theme took over 30 s once on a busy CI runner (PR 246); a hang still ends the run.
        if (!ready.Wait(_desktopWait + TimeSpan.FromMinutes(2)))
        {
            throw new InvalidOperationException("The WPF test thread did not start.");
        }

        if (_dispatcher is null)
        {
            throw new InvalidOperationException($"Another Workbench test run kept the desktop for over {_desktopWait.TotalMinutes} minutes.");
        }

        WindowsAtStart = Run(() => Application.Current.Windows.Count);
        MainWindowAtStart = Run(() => Application.Current.MainWindow);
    }

    /// <summary>The application's windows once its dispatcher ran, before any test: the App's startup would show one.</summary>
    internal int WindowsAtStart { get; }

    /// <summary>The application's main window at the same moment, which only the App's startup sets.</summary>
    internal Window? MainWindowAtStart { get; }

    /// <summary>The UI thread's dispatcher, which the app posts the queue's changes to.</summary>
    internal Dispatcher Dispatcher => Ui();

    /// <summary>Runs on the UI thread, rethrowing on the caller's; work the test left queued finishes first.</summary>
    public void Run(Action action)
    {
        try
        {
            Ui().Invoke(action);
        }
        finally
        {
            Ui().Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    }

    public T Run<T>(Func<T> action)
    {
        try
        {
            return Ui().Invoke(action);
        }
        finally
        {
            Ui().Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    }

    /// <summary>Runs with the language forced, then restores it: the catalogue is one per process.</summary>
    public void RunWithLanguage(AppLanguage language, Action action) => Run(() =>
    {
        AppLanguage previous = Loc.Current.Language;
        Loc.Current.Language = language;
        try
        {
            action();
        }
        finally
        {
            Loc.Current.Language = previous;
        }
    });

    /// <summary>
    /// Lets the dispatcher finish layout, rendering and the work queued behind them, including what that work queues in
    /// turn, such as the guard's start after a queue change the app posted rendered (APR-04): until the application idles.
    /// </summary>
    internal static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    /// <summary>Waits for the dispatcher to close its windows: a host that exits first reports a crash.</summary>
    public void Dispose()
    {
        if (_dispatcher is null)
        {
            return;
        }

        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(30));
    }

    private Dispatcher Ui() => _dispatcher ?? throw new InvalidOperationException("The WPF test thread has no dispatcher.");

    /// <summary>
    /// The real <see cref="App"/>, with App.xaml's resources but without its startup, which the Application constructor
    /// queues on the dispatcher: aborted, it keeps the tests off the engineer's data root (settings, logs), the
    /// single-instance mutex, the approval pipe and the tray, and a second run from ending this one's app.
    /// </summary>
    private static void CreateApp(Dispatcher dispatcher)
    {
        var queued = new List<DispatcherOperation>();
        DispatcherHookEventHandler posted = (_, e) => queued.Add(e.Operation);
        dispatcher.Hooks.OperationPosted += posted;
        App app;
        try
        {
#pragma warning disable CA2000 // the App is Application.Current for the rest of the process, as in the product
            app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
#pragma warning restore CA2000
        }
        finally
        {
            dispatcher.Hooks.OperationPosted -= posted;
        }

        queued.ForEach(operation => operation.Abort());
        app.InitializeComponent();
    }

    private static bool Take(Mutex desktop)
    {
        try
        {
            return desktop.WaitOne(_desktopWait);
        }
        catch (AbandonedMutexException)
        {
            return true; // a run that crashed left it, and it is this run's now
        }
    }
}

[CollectionDefinition(Name)]
public sealed class WpfTestGroup : ICollectionFixture<WpfContext>
{
    public const string Name = "wpf";
}
