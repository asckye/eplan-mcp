using System.Windows;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench;

/// <summary>The Workbench process: theme, language and the main window.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WorkbenchFonts.Apply(Resources, AppContext.BaseDirectory);
        Loc.Current.Language = Loc.FromSystem();
        ThemeManager.Current.Initialize(AppTheme.Auto);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
