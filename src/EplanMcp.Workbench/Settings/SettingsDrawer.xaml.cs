using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Settings;

/// <summary>The settings drawer; the window shows it over a scrim and gives focus back when it closes (DLG-10).</summary>
public partial class SettingsDrawer : UserControl
{
    /// <summary>The most of config.json shown; the rest is for its own program.</summary>
    private const int MaxConfig = 64 * 1024;

    private StartAtLogin? _login;
    private string? _config;
    private IShellOpen _shell = new ShellOpen();
    private bool _showing;

    public SettingsDrawer() => InitializeComponent();

    /// <summary>The close button or Esc asked to close the drawer.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Focus goes to the drawer's title when it opens (DLG-10).</summary>
    public void FocusHeading() => Keyboard.Focus(Heading);

    /// <summary>The login entry and the Host's config.json; for tests also the shell that opens it.</summary>
    internal void Attach(StartAtLogin login, string configFile, IShellOpen? shell = null)
    {
        (_login, _config) = (login, configFile);
        _shell = shell ?? _shell;
        Refresh();
    }

    /// <summary>Reads both again as the drawer opens: either may have changed meanwhile, by the installer or by hand.</summary>
    internal void Refresh()
    {
        Show(_login?.IsOn == true);
        LoginError.Visibility = Visibility.Collapsed;
        ConfigPath.Text = _config ?? string.Empty;
        ConfigText.Text = _config is null ? string.Empty : Config(_config);
        ConfigButton.IsEnabled = File.Exists(_config);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The switch applies at once, however it was turned: click, keys or UI Automation (CMD-08); when Windows refuses,
    /// it goes back and says so in place (ERR-03).
    /// </summary>
    private void OnLogin(object sender, RoutedEventArgs e)
    {
        if (_showing)
        {
            return;
        }

        bool on = LoginSwitch.IsChecked == true;
        bool done = _login?.Set(on) == true;
        Show(done ? on : !on);
        LoginError.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        if (!done)
        {
            AccessibleStatus.Announce(LoginError);
        }
    }

    private void OnOpenConfig(object sender, RoutedEventArgs e) => _shell.Open(_config!);

    /// <summary>Sets the switch to what is so, without that counting as the engineer turning it.</summary>
    private void Show(bool on)
    {
        _showing = true;
        LoginSwitch.IsChecked = on;
        _showing = false;
    }

    /// <summary>config.json as plain text, line by line (CTL-12), read while the Host may write it; never written here.</summary>
    private static string Config(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var text = new char[MaxConfig];
            string read = new(text, 0, reader.ReadBlock(text));
            return string.Join(Environment.NewLine, read.Split('\n').Select(line => ProjectText.Line(line.TrimEnd('\r'))));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Loc.Current["Settings.NoConfig"];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Loc.Current.T("Settings.ConfigUnreadable", ex.Message);
        }
    }
}
