using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Audit;
using EplanMcp.Workbench.Clients;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Logging;
using EplanMcp.Workbench.Logs;
using EplanMcp.Workbench.Project;
using EplanMcp.Workbench.Protocol;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Status;
using EplanMcp.Workbench.Themes;
using Microsoft.Extensions.Logging;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench;

/// <summary>
/// The Workbench process (M5 design 4.4): one per user and data root; settings, fonts, language, theme, dual accent,
/// normal-write approval policy, the approval pipe and its queue, the 8-page IDE window and the tray.
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
    private SafetyPolicyStore? _policy;
    private bool _listening;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private Vm.VmView? _vm;
    private Project.HostToolClient? _tools;

    internal int Pending { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new SingleInstance(WorkbenchPipeName.ForCurrentUser(_locations.Root));
        if (!_instance.IsFirst)
        {
            _instance.SignalFirst();
            Shutdown();
            return;
        }

        _settings = WorkbenchSettings.Load(_locations.Settings);
        WorkbenchFonts.Apply(Resources);
        Loc.Current.Language = _settings.Language ?? Loc.FromSystem();
        // Light / Dark only in UI (Auto normalized to Light/Dark while High Contrast stays automatic).
        AppTheme startupTheme = _settings.Theme == AppTheme.Dark ? AppTheme.Dark : AppTheme.Light;
        ThemeManager.Current.Initialize(startupTheme);
        AccentManager.Current.Initialize(_settings.Accent);

        Loc.Current.LanguageChanged += (_, _) => Remember(_settings with { Language = Loc.Current.Language });
        ThemeManager.Current.PropertyChanged += OnThemeChanged;
        AccentManager.Current.PropertyChanged += OnAccentChanged;

        _policy = new SafetyPolicyStore(_locations.Config);
        _policy.Load();

        _window = new MainWindow { HideOnClose = true };
        MainWindow = _window;
        _tray = new TrayIcon(() => _window.Open(null), () => _ = ExitAsync());
        _window.NotificationWanted += item => _tray.Remind(_queue?.PendingCount ?? 1, item.Tool, () => _window.Open(item));
        _instance.Listen(() => Dispatcher.BeginInvoke(Restore));

        _log = new FileLog(_locations.Logs, TimeProvider.System);
        _uiLogger = _log.CreateLogger("Workbench");
        DispatcherUnhandledException += OnUnhandled;
        // The Host decides whether a write needs approval, from config.json at the moment of the write (ADR-0061).
        // The queue never answers for the engineer: whatever the Host still asks about is shown.
        _queue = new ApprovalQueue(TimeProvider.System);
        _policy.Changed += () =>
        {
            if (_policy is not null)
            {
                Remember(_settings with { RequireNormalWriteApproval = _policy.RequireNormalWriteApproval });
            }
        };

        _view = new ApprovalQueueView(_queue, TimeProvider.System);
        _view.Follow(Dispatcher, ShowQueue);
        _window.Attach(_view);
        _window.AttachPolicy(_policy, _locations.Audit);

        string? hostExecutable = ProgramLocation.FindHost(AppContext.BaseDirectory);
        IHostRunner? runner = hostExecutable is not null ? new HostProcess(hostExecutable) : null;
        var host = new HostCommands(runner);
        var clients = new ClientsView(host, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _vm = new Vm.VmView(runner, new Vm.VmElevation(), Vm.VmNetwork.Addresses());

        // ADR-0065: the project page reads through the Workbench's own Host, started on its first read.
        _tools = hostExecutable is not null ? new Project.HostToolClient(hostExecutable) : null;
        _window.AttachProject(new ProjectView(_tools));
        _window.AttachAudit(new AuditView(_locations.Audit, host, TimeProvider.System));
        _window.AttachStatus(new StatusView(host, () => _listening && _queue.IsAccepting));
        _window.AttachClients(clients);
        _window.AttachMcp(_vm, clients, hostExecutable);
        _window.AttachLogs(new LogsView(_locations.Logs, _locations.Config));
        _window.AttachSettings(StartAtLogin.ForCurrentUser(), _locations.Config, _policy);

        _server = new ApprovalServer(_queue, _locations.Root, TimeProvider.System, _log.CreateLogger("Approvals"));
        _listening = _server.Start();
        ShowQueue();
        _window.Show();
    }

    public void Dispose()
    {
        _vm?.Dispose();
        // The Host child ends with the Workbench; a Host that does not stop in two seconds is left to the job's end.
        _tools?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
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

    private void ShowQueue()
    {
        Pending = _queue?.PendingCount ?? 0;
        _tray?.Pending = Pending;
        if (_queue is not null)
        {
            _window?.ShowQueue(_queue, _listening);
        }
    }

    private async Task ExitAsync()
    {
        int pending = Pending;
        if (pending > 0)
        {
            bool confirmed;
            try
            {
                Restore();
                Loc loc = Loc.Current;
                confirmed = WorkbenchDialog.Confirm(_window!, loc["Exit.Title"], loc.T("Exit.Message", pending), loc["Exit.Cancel"], loc["Exit.Confirm"]);
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Trace.TraceWarning("Exit confirmation unavailable: " + ex.GetType().Name);
                confirmed = false;
            }

            if (!confirmed)
            {
                return;
            }
        }

        try
        {
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

    private void OnAccentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccentManager.Accent))
        {
            Remember(_settings with { Accent = AccentManager.Current.Accent });
        }
    }

    private void Remember(WorkbenchSettings settings)
    {
        _settings = settings;
        settings.Save(_locations.Settings);
    }
}
