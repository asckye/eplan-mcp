using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Audit;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Logging;
using EplanMcp.Workbench.Protocol;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using Microsoft.Extensions.Logging;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench;

/// <summary>
/// The Workbench process (M5 design 4.4): one per user and data root; settings, fonts, language, theme, the approval
/// pipe and its queue, the window and the tray. Closing the window only hides it to the tray; exiting goes through the
/// tray menu (NAV-09, DLG-11).
/// </summary>
public sealed partial class App : Application, IDisposable
{
    private readonly WorkbenchDataLocations _locations = WorkbenchDataLocations.FromEnvironment();
    private WorkbenchSettings _settings = new();
    private SingleInstance? _instance;
    private FileLog? _log;
    private ILogger? _uiLogger;
    private ApprovalQueue? _queue;
    private ApprovalServer? _server;
    private ApprovalQueueView? _view;
    private bool _listening;
    private TrayIcon? _tray;
    private MainWindow? _window;

    /// <summary>How many questions wait now; exiting with any asks first (DLG-11).</summary>
    internal int Pending { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new SingleInstance(WorkbenchPipeName.ForCurrentUser(_locations.Root));
        if (!_instance.IsFirst)
        {
            _instance.SignalFirst(); // the Workbench that runs comes to the front; this one ends
            Shutdown();
            return;
        }

        _settings = WorkbenchSettings.Load(_locations.Settings);
        WorkbenchFonts.Apply(Resources, AppContext.BaseDirectory);
        Loc.Current.Language = _settings.Language ?? Loc.FromSystem();
        ThemeManager.Current.Initialize(_settings.Theme);

        // From here on, what the engineer picks in the settings drawer is remembered.
        Loc.Current.LanguageChanged += (_, _) => Remember(_settings with { Language = Loc.Current.Language });
        ThemeManager.Current.PropertyChanged += OnThemeChanged;

        _window = new MainWindow { HideOnClose = true };
        MainWindow = _window;
        _tray = new TrayIcon(() => _window.Open(null), () => _ = ExitAsync());
        _window.NotificationWanted += item => _tray.Remind(_queue?.PendingCount ?? 1, item.Tool, () => _window.Open(item));
        _instance.Listen(() => Dispatcher.BeginInvoke(Restore));

        _log = new FileLog(_locations.Logs, TimeProvider.System);
        _uiLogger = _log.CreateLogger("Workbench");
        DispatcherUnhandledException += OnUnhandled;
        _queue = new ApprovalQueue(TimeProvider.System);
        _view = new ApprovalQueueView(_queue, TimeProvider.System);
        _view.Follow(Dispatcher, ShowQueue);
        _window.Attach(_view);
        var host = new HostCommands(HostLocator.Find(AppContext.BaseDirectory) is { } executable ? new HostProcess(executable) : null);
        _window.AttachAudit(new AuditView(_locations.Audit, host, TimeProvider.System));
        _server = new ApprovalServer(_queue, _locations.Root, TimeProvider.System, _log.CreateLogger("Approvals"));
        _listening = _server.Start();
        ShowQueue();
        _window.Show();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _view?.Dispose();
        _log?.Dispose();
        _instance?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    private void Restore() => _window?.Restore();

    /// <summary>
    /// A fault in a page is logged and the Workbench goes on: ending it would close every waiting question (DLG-11) and
    /// send the next ones to the EPLAN dialog for no reason of the engineer's.
    /// </summary>
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_uiLogger is not null)
        {
            LogUnhandled(_uiLogger, e.Exception);
        }

        e.Handled = true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on the UI thread; the Workbench goes on")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);

    /// <summary>The waiting count in the tray and the Header, and whether questions are taken (STS-09 to STS-11).</summary>
    private void ShowQueue()
    {
        Pending = _queue?.PendingCount ?? 0;
        _tray?.Pending = Pending;
        if (_queue is not null)
        {
            _window?.ShowQueue(_queue, _listening);
        }
    }

    /// <summary>
    /// Exit from the tray: with questions waiting, only after the engineer confirms (DLG-11); they then return to their
    /// Hosts as closed.
    /// </summary>
    private async Task ExitAsync()
    {
        try
        {
            int pending = Pending;
            if (pending > 0)
            {
                Restore();
                Loc loc = Loc.Current;
                if (!WorkbenchDialog.Confirm(_window!, loc["Exit.Title"], loc.T("Exit.Message", pending), loc["Exit.Cancel"], loc["Exit.Confirm"]))
                {
                    return;
                }
            }

            if (_server is not null)
            {
                await _server.DisposeAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // Quitting goes on: a Host whose connection breaks takes its question as closed all the same.
        }

        if (_window is not null)
        {
            _window.HideOnClose = false;
            _window.Close();
        }

        Shutdown();
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeManager.Theme))
        {
            Remember(_settings with { Theme = ThemeManager.Current.Theme });
        }
    }

    private void Remember(WorkbenchSettings settings)
    {
        _settings = settings;
        settings.Save(_locations.Settings);
    }
}
