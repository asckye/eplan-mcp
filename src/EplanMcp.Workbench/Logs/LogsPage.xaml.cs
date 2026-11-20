using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Logs;

public partial class LogsPage : UserControl, IWorkbenchPage
{
    private LogsView? _view;
    private IShellOpen _shell = new ShellOpen();
    private bool _loadedOnce;

    public LogsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Loc.Current.LanguageChanged -= OnLanguageChanged;
            Loc.Current.LanguageChanged += OnLanguageChanged;
            ApplyTexts();
            Show();
        };
        Unloaded += (_, _) => Loc.Current.LanguageChanged -= OnLanguageChanged;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && !_loadedOnce)
            {
                _loadedOnce = true;
                Refresh();
            }
        };
        ApplyTexts();
    }

    public event Action? StorageSettingsRequested;

    public FrameworkElement? Heading => PageTitle;

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    internal void Attach(LogsView view, IShellOpen? shell = null)
    {
        _view = view;
        _shell = shell ?? _shell;
        view.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Show);
        FolderFooter.Text = view.LogsFolder;
        Show();
    }

    internal void Refresh() => _view?.Reload();

    internal void ExportDiagnostics()
    {
        if (_view is null)
        {
            return;
        }

        try
        {
            string zip = _view.ExportDiagnosticsZip();
            BannerText.Text = Loc.Current.T("Logs.DiagCreated", zip);
            Banner.Visibility = Visibility.Visible;
            AccessibleStatus.Announce(BannerText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BannerText.Text = Loc.Current.T("Logs.DiagFailed", ex.Message);
            Banner.Visibility = Visibility.Visible;
            AccessibleStatus.Announce(BannerText);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyTexts();
        Show();
    }

    private void ApplyTexts()
    {
        Loc loc = Loc.Current;
        ColTime.Header = loc["Logs.ColTime"];
        ColLevel.Header = loc["Logs.ColLevel"];
        ColSource.Header = loc["Logs.ColModule"];
        ColMessage.Header = loc["Logs.ColMessage"];
    }

    private void Show()
    {
        if (_view is not { } view)
        {
            return;
        }

        LogList.ItemsSource = view.Visible;
        Empty.Visibility = view.Visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_view is null || FilterAll is null || SearchBox is null)
        {
            return;
        }

        string level = FilterError.IsChecked == true ? "ERROR" : FilterWarn.IsChecked == true ? "WARN" : "ALL";
        _view.SetFilter(level, SearchBox.Text);
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_view is not { Visible.Count: > 0 } view)
        {
            return;
        }

        string text = string.Join(Environment.NewLine, view.Visible.Select(r => $"{r.Time} [{r.Level}] {r.Source}: {r.Message}"));
        Clipboard.SetText(text);
        BannerText.Text = Loc.Current.T("Logs.Copied", view.Visible.Count);
        Banner.Visibility = Visibility.Visible;
        AccessibleStatus.Announce(BannerText);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (_view is { } view)
        {
            Directory.CreateDirectory(view.LogsFolder);
            _shell.Open(view.LogsFolder);
        }
    }

    private void OnExportDiag(object sender, RoutedEventArgs e) => ExportDiagnostics();

    private void OnGoStorage(object sender, RoutedEventArgs e) => StorageSettingsRequested?.Invoke();
}
