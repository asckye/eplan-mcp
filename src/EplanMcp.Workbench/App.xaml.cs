using System.ComponentModel;
using System.Windows;
using EplanMcp.Workbench.Protocol;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench;

/// <summary>
/// The Workbench process (M5 design 4.4): one per user and data root; settings, fonts, language, theme, the window and
/// the tray. Closing the window only hides it to the tray; exiting goes through the tray menu (NAV-09, DLG-11).
/// </summary>
public sealed partial class App : Application, IDisposable
{
    private readonly WorkbenchDataLocations _locations = WorkbenchDataLocations.FromEnvironment();
    private WorkbenchSettings _settings = new();
    private SingleInstance? _instance;
    private TrayIcon? _tray;
    private MainWindow? _window;

    /// <summary>How many questions wait now; exiting with any asks first (DLG-11).</summary>
    internal int Pending { get; set; }

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
        _tray = new TrayIcon(Restore, RequestExit);
        _instance.Listen(() => Dispatcher.BeginInvoke(Restore));
        _window.Show();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _instance?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    private void Restore() => _window?.Restore();

    /// <summary>Exit from the tray: with questions waiting, only after the engineer confirms (DLG-11).</summary>
    private void RequestExit()
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
