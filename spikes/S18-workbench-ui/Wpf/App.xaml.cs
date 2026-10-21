using System.IO;
using System.Windows;
using System.Windows.Media;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Spikes.S18.Wpf;

public partial class App : Application
{
    public App()
    {
        Options = SpikeOptions.Parse(Environment.GetCommandLineArgs());
        SpikeLog.Init(Options.LogPath);
        SpikeLog.Write("main");
        DispatcherUnhandledException += (_, e) => SpikeLog.Write("unhandled", e.Exception.ToString().ReplaceLineEndings(" | "));
    }

    /// <summary>The app's Fonts folder as a base URI (with the trailing separator WPF needs).</summary>
    internal static Uri FontFolder { get; } = new(Path.Combine(AppContext.BaseDirectory, "Fonts") + Path.DirectorySeparatorChar);

    internal SpikeOptions Options { get; }

    /// <summary>The family found in Fonts\ (Maple Mono CN), or null when the folder has no font.</summary>
    internal string? BundledFamily { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyFonts();
        ThemeManager.Current.Initialize(Options.Theme switch { "light" => AppTheme.Light, "dark" => AppTheme.Dark, _ => AppTheme.Auto });
        if (Options.Theme == "hc")
        {
            ThemeManager.Current.HighContrastOverride = true;
        }

        SpikeLog.Write("launched");
        var window = new MainWindow(Options);
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Ui.Font and Ui.FontMono (Primer.xaml reads both with DynamicResource) become Maple Mono CN from the app folder:
    /// a FontFamily with the folder as base URI and "./#family", so WPF takes every weight of the family in the folder.
    /// Without a font file the Primer.xaml fallbacks stay.
    /// </summary>
    private void ApplyFonts()
    {
        FontFamily? found = Directory.Exists(FontFolder.LocalPath) ? Fonts.GetFontFamilies(FontFolder).FirstOrDefault() : null;
        string? name = found?.FamilyNames.Values.FirstOrDefault();
        if (name is null)
        {
            SpikeLog.Write("font", "no font in Fonts, fallback " + ((FontFamily)Resources["Ui.Font"]).Source);
            return;
        }

        BundledFamily = name;
        var family = new FontFamily(FontFolder, $"./#{name}, Segoe UI, Microsoft YaHei UI");
        Resources["Ui.Font"] = family;
        Resources["Ui.FontMono"] = family;
        SpikeLog.Write("font", $"{family.BaseUri} {family.Source}");
    }
}
