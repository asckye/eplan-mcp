using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace EplanMcp.Spikes.S18.WinUi;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        UnhandledException += (_, e) => SpikeLog.Write("unhandled", e.Exception.ToString().ReplaceLineEndings(" | "));
        InitializeComponent();
    }

    internal static App? Instance => Current as App;

    internal BundledFontFile? Font { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        SpikeLog.Write("launched");

        // Before the window is parsed: WorkbenchFontFamily and WorkbenchStrongFontFamily feed the text styles in MainWindow.xaml, and
        // ContentControlThemeFontFamily (a WinUI theme resource) feeds the built-in controls: buttons, radio buttons.
        // Not in the constructor: Application.Resources throws E_UNEXPECTED there (0x8000FFFF, seen with 2.5.1).
        Font = BundledFont.Find();
        var family = new FontFamily(BundledFont.Source(Font));
        Resources["WorkbenchFontFamily"] = family;
        Resources["ContentControlThemeFontFamily"] = family;
        Resources["WorkbenchStrongFontFamily"] = new FontFamily(BundledFont.Source(Font, strong: true));
        SpikeLog.Write("font", family.Source + " | strong: " + BundledFont.Source(Font, strong: true));
        _window = new MainWindow(Program.Options);
        _window.Activate();
    }

    /// <summary>A second instance redirected its activation here (AppInstance); bring the window back.</summary>
    internal void OnRedirected() => _window?.DispatcherQueue.TryEnqueue(() => _window.ShowFromOutside("redirect"));
}
