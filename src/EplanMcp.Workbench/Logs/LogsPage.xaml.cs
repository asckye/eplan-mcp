using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Shell;
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
    }

    public event Action? StorageSettingsRequested;

    public FrameworkElement? Heading => PageTitle;

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    internal void Attach(LogsView view, IShellOpen? shell = null)
    {
        _view = view;
        _shell = shell ?? _shell;
        view.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Show);
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
            Toasts.Show(Loc.Current.T("Logs.DiagCreated", _view.ExportDiagnosticsZip()), ToastTone.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toasts.Show(Loc.Current.T("Logs.DiagFailed", ex.Message), ToastTone.Error);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        if (_view is not { } view)
        {
            return;
        }

        LogList.ItemsSource = view.Visible;
        Empty.Visibility = view.Visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = view.Visible.Count > 0;
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_view is null || FilterAll is null)
        {
            return;
        }

        string level = FilterError.IsChecked == true ? "ERROR" : FilterWarn.IsChecked == true ? "WARN" : "ALL";
        _view.SetFilter(level, string.Empty);
    }

    /// <summary>Asks first, then hides the entries shown; the log files stay (Workbench 4.0 design); a test replaces the question.</summary>
    internal Func<int, bool>? ConfirmClear { get; set; }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_view is not { Visible.Count: > 0 } view)
        {
            return;
        }

        Loc loc = Loc.Current;
        int count = view.Visible.Count;
        bool yes = (ConfirmClear ?? (n => WorkbenchDialog.Confirm(Window.GetWindow(this)!, loc["Logs.ClearTitle"], loc["Logs.ClearMessage"], loc["Logs.ClearCancel"], loc["Logs.ClearAction"],
            loc.T("Logs.ClearDetails", n, view.LogsFolder))))(count);
        if (yes)
        {
            view.ClearShown();
            Toasts.Show(loc["Logs.Cleared"], ToastTone.Success);
        }
    }

    private void OnExportDiag(object sender, RoutedEventArgs e) => ExportDiagnostics();

    private void OnGoStorage(object sender, RoutedEventArgs e) => StorageSettingsRequested?.Invoke();
}
