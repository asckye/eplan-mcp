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
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;

    public WpfContext()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            if (Application.Current is null)
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
            }

            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "wpf-test-ui",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new InvalidOperationException("The WPF test thread did not start.");
        }
    }

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

    /// <summary>Lets the dispatcher finish layout, rendering and the work queued behind them.</summary>
    internal static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

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
}

[CollectionDefinition(Name)]
public sealed class WpfTestGroup : ICollectionFixture<WpfContext>
{
    public const string Name = "wpf";
}
