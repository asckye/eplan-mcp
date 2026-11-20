using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Settings;

/// <summary>
/// Dedicated Workbench Settings page (TIA 1:1 3-category layout: Safety &amp; Approvals, Appearance &amp; Language, Storage &amp; About).
/// </summary>
public partial class SettingsPage : UserControl, IWorkbenchPage
{
    private const int MaxConfig = 64 * 1024;

    private StartAtLogin? _login;
    private SafetyPolicyStore? _policy;
    private string? _config;
    private string _rootFolder = string.Empty;
    private IShellOpen _shell = new ShellOpen();
    private bool _showing;

    public SettingsPage()
    {
        InitializeComponent();
        string version = typeof(SettingsPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.8.0";
        VersionLine.Text = "EplanMcp Workbench v" + version;
    }

    public FrameworkElement? Heading => PageTitle;

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    internal void SetLeftPaneVisible(bool visible)
    {
        LeftPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LeftCol.Width = visible ? new GridLength(236) : new GridLength(0);
    }

    internal void OpenStorageCategory() => NavStorage.IsChecked = true;

    internal void Attach(StartAtLogin login, string configFile, SafetyPolicyStore? policy = null, IShellOpen? shell = null)
    {
        _login = login;
        _config = configFile;
        _policy = policy ?? new SafetyPolicyStore(configFile);
        _shell = shell ?? _shell;
        _rootFolder = Path.GetDirectoryName(configFile) ?? string.Empty;
        _policy.Changed += () => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    internal void Refresh()
    {
        _showing = true;
        try
        {
            LoginSwitch.IsChecked = _login?.IsOn == true;
            if (_policy is not null)
            {
                ApprovalSwitch.IsChecked = _policy.RequireNormalWriteApproval;
                ApprovalSwitch.IsEnabled = !_policy.WritesDenied;
                Timeout60.IsChecked = _policy.ApprovalTimeoutSeconds == 60;
                Timeout120.IsChecked = _policy.ApprovalTimeoutSeconds is not (60 or 300);
                Timeout300.IsChecked = _policy.ApprovalTimeoutSeconds == 300;
            }
        }
        finally
        {
            _showing = false;
        }

        LoginError.Visibility = Visibility.Collapsed;
        DirRootPath.Text = _rootFolder;
        DirAuditPath.Text = _rootFolder.Length > 0 ? Path.Combine(_rootFolder, "audit") : string.Empty;
        DirLogsPath.Text = _rootFolder.Length > 0 ? Path.Combine(_rootFolder, "logs") : string.Empty;
        ConfigPath.Text = _config ?? string.Empty;
        ConfigText.Text = _config is null ? string.Empty : Config(_config);
        ConfigButton.IsEnabled = File.Exists(_config);
        ConfigProblem.Visibility = Visibility.Collapsed;
    }

    private void OnCategoryChanged(object sender, RoutedEventArgs e)
    {
        if (SafetySection is null)
        {
            return;
        }

        SafetySection.Visibility = NavSafety.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AppearanceSection.Visibility = NavAppearance.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        StorageSection.Visibility = NavStorage.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnApprovalChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || _policy is null)
        {
            return;
        }

        bool? answer = ApprovalPolicyUi.Set(_policy, ApprovalSwitch.IsChecked == true, this);
        bool saved = answer != false;
        if (answer != true)
        {
            // Nothing changed: show what the file says again.
            _showing = true;
            ApprovalSwitch.IsChecked = _policy.RequireNormalWriteApproval;
            _showing = false;
        }

        PolicyError.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        if (!saved)
        {
            AccessibleStatus.Announce(PolicyError);
        }
    }

    private void OnTimeoutChanged(object sender, RoutedEventArgs e)
    {
        if (_showing || _policy is null || sender is not RadioButton { Tag: string raw })
        {
            return;
        }

        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
        {
            bool saved = _policy.SetApprovalTimeoutSeconds(seconds);
            PolicyError.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void OnLogin(object sender, RoutedEventArgs e)
    {
        if (_showing)
        {
            return;
        }

        bool on = LoginSwitch.IsChecked == true;
        bool done = _login?.Set(on) == true;
        _showing = true;
        LoginSwitch.IsChecked = done ? on : !on;
        _showing = false;
        LoginError.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        if (!done)
        {
            AccessibleStatus.Announce(LoginError);
        }
    }

    private void OnOpenConfig(object sender, RoutedEventArgs e)
    {
        if (_config is null)
        {
            return;
        }

        bool opened = _shell.Open(_config);
        ConfigProblem.Text = opened ? string.Empty : Loc.Current.T("Settings.OpenFailed", _config);
        ConfigProblem.Visibility = opened ? Visibility.Collapsed : Visibility.Visible;
        if (!opened)
        {
            AccessibleStatus.Announce(ConfigProblem);
        }
    }

    private void OnOpenRoot(object sender, RoutedEventArgs e) => OpenFolder(_rootFolder);

    private void OnOpenAudit(object sender, RoutedEventArgs e) => OpenFolder(DirAuditPath.Text);

    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(DirLogsPath.Text);

    private void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            _shell.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore unreadable directory creation in read-only test contexts.
        }
    }

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
