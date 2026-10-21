using System.ComponentModel;
using System.Windows;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench;

/// <summary>The Workbench process: settings, fonts, language, theme and the main window.</summary>
public partial class App : Application
{
    private readonly WorkbenchDataLocations _locations = WorkbenchDataLocations.FromEnvironment();
    private WorkbenchSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _settings = WorkbenchSettings.Load(_locations.Settings);
        WorkbenchFonts.Apply(Resources, AppContext.BaseDirectory);
        Loc.Current.Language = _settings.Language ?? Loc.FromSystem();
        ThemeManager.Current.Initialize(_settings.Theme);

        // From here on, what the engineer picks in the settings drawer is remembered.
        Loc.Current.LanguageChanged += (_, _) => Remember(_settings with { Language = Loc.Current.Language });
        ThemeManager.Current.PropertyChanged += OnThemeChanged;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
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
