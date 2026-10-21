using System.Windows;

namespace EplanMcp.Spikes.S18.WpfFluent;

public partial class App : Application
{
    public App()
    {
        Options = SpikeOptions.Parse(Environment.GetCommandLineArgs());
        SpikeLog.Init(Options.LogPath);
        SpikeLog.Write("main");
        DispatcherUnhandledException += (_, e) => SpikeLog.Write("unhandled", e.Exception.ToString().ReplaceLineEndings(" | "));
    }

    internal SpikeOptions Options { get; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SpikeLog.Write("launched");
        var window = new MainWindow(Options);
        MainWindow = window;
        window.Show();
    }
}
