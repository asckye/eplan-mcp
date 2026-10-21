using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Audit;

/// <summary>
/// The audit page over its <see cref="AuditView"/>: reads the log the first time it is shown and on F5 or "Reload",
/// never on its own (M5 design 4.4); a read that fails or an integrity check says so in the Banner (ERR-04, STS-02).
/// </summary>
public partial class AuditPage : UserControl, IWorkbenchPage
{
    private AuditView? _view;
    private IShellOpen _shell = new ShellOpen();
    private Task? _reading;
    private bool _choosing;
    private bool _queued;
    private IReadOnlyList<AuditRecord>? _listed;
    private (int Reads, AppLanguage Language)? _filled;

    public AuditPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && _reading is null)
            {
                Refresh();
            }
        };
        Loaded += (_, _) =>
        {
            Loc.Current.LanguageChanged -= OnLanguageChanged;
            Loc.Current.LanguageChanged += OnLanguageChanged;
            Show();
        };
        Unloaded += (_, _) => Loc.Current.LanguageChanged -= OnLanguageChanged;
    }

    public FrameworkElement? Heading => PageTitle;

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    /// <summary>Gives the page its state and, for tests, the shell that opens the folder.</summary>
    internal void Attach(AuditView view, IShellOpen? shell = null)
    {
        _view = view;
        _shell = shell ?? _shell;
        view.PropertyChanged += OnViewChanged;
        Show();
    }

    /// <summary>Reads the log again (F5, KEY-07); the filters stay.</summary>
    internal void Refresh()
    {
        if (_view is not null && _reading is not { IsCompleted: false })
        {
            _reading = _view.LoadAsync(CancellationToken.None);
        }
    }

    /// <summary>Ctrl+F: to the first filter (KEY-07).</summary>
    internal void FocusFilters() => Keyboard.Focus(DayFilter);

    /// <summary>
    /// Progress moves only the bar. Any other change shows the page once, however many arrive before that runs: below
    /// input and rendering (Background), so that a long log never holds up the approval queue's countdowns.
    /// </summary>
    private void OnViewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuditView.Progress))
        {
            WorkingBar.Value = _view?.Progress ?? 0;
        }
        else if (!_queued)
        {
            _queued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _queued = false;
                Show();
            }, DispatcherPriority.Background);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _listed = null; // the rows' texts follow the language
        Show();
    }

    private void Show()
    {
        Loc loc = Loc.Current;
        (TimeColumn.Header, KindColumn.Header, ToolColumn.Header, ProjectColumn.Header, ResultColumn.Header) =
            (loc["Audit.Time"], loc["Audit.Kind"], loc["Audit.Tool"], loc["Audit.Project"], loc["Audit.Result"]);
        ReloadButton.ToolTip = loc.T("Shell.Shortcut", loc["Audit.Reload"], "F5");
        if (_view is not { } view)
        {
            return;
        }

        bool loading = view.State == AuditState.Loading;
        Caption.Text = loading ? loc["Audit.Reading"] : loc.T("Audit.Count", view.Shown.Count, view.Total);
        ShowWorking(loading ? loc["Audit.Reading"] : view.IsVerifying ? loc["Audit.Verifying"] : null, loading ? view.Progress : null);
        ShowBanner(view);
        NotRecordsBanner.Visibility = view.State == AuditState.Ready && view.NotRecords > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotRecordsText.Text = loc.T("Audit.NotRecords", view.NotRecords);
        if (_filled != (view.Reads, loc.Language))
        {
            // Only after a reading or in another language: a choice in a filter must not refill the list it is in.
            _filled = (view.Reads, loc.Language);
            Fill(DayFilter, view.DayOptions, view.Day);
            Fill(ToolFilter, view.ToolOptions, view.Tool);
            Fill(ProjectFilter, view.ProjectOptions, view.Project);
            Fill(DecisionFilter, view.DecisionOptions, view.Decision);
            Fill(ExecutionFilter, view.ExecutionOptions, view.Execution);
        }

        List(view);
        Empty.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        NoMatch.Visibility = view.State == AuditState.Ready && !view.IsEmpty && view.Shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Records.Visibility = view.Shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Filters.IsEnabled = view.State == AuditState.Ready && !view.IsEmpty;
        ReloadButton.IsEnabled = !loading;

        // NAV-05, CMD-04: a command that cannot run says why.
        string? noVerify = view.CanVerify ? null : loc["Host.Missing"];
        VerifyButton.IsEnabled = noVerify is null && !view.IsVerifying;
        Explain(VerifyButton, noVerify ?? loc["Audit.VerifyHelp"]);
        bool folder = Directory.Exists(view.Folder);
        FolderButton.IsEnabled = folder;
        Explain(FolderButton, folder ? view.Folder : loc.T("Audit.NoFolder", view.Folder));
    }

    /// <summary>
    /// The rows, made again only when the records shown changed; the row chosen stays chosen, and keeps the focus it had,
    /// so that a drawer it opened can give the focus back to it (DLG-10).
    /// </summary>
    private void List(AuditView view)
    {
        if (ReferenceEquals(_listed, view.Shown))
        {
            return;
        }

        AuditRecord? chosen = (Records.SelectedItem as AuditItem)?.Record;
        bool focused = Records.IsKeyboardFocusWithin;
        _listed = view.Shown;
        List<AuditItem> rows = [.. view.Shown.Select(r => new AuditItem(r, view.LocalTime(r)))];
        Records.ItemsSource = rows;
        if (rows.FirstOrDefault(row => row.Record == chosen) is { } again)
        {
            Records.SelectedItem = again;
            if (focused)
            {
                Records.UpdateLayout();
                (Records.ItemContainerGenerator.ContainerFromItem(again) as UIElement)?.Focus();
            }
        }
    }

    private void ShowWorking(string? text, double? progress)
    {
        Working.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        WorkingText.Text = text ?? string.Empty;
        WorkingBar.IsIndeterminate = progress is null;
        WorkingBar.Value = progress ?? 0;
    }

    /// <summary>The read failure, else the last verification: severity in icon and words (ERR-07), the details below (ERR-02).</summary>
    private void ShowBanner(AuditView view)
    {
        (string? text, string? details, bool failed) = view.State == AuditState.Failed
            ? (Loc.Current.T("Audit.Unreadable", view.Folder), view.Problem, true)
            : (view.Verification, view.VerificationDetails, view.VerificationFailed);
        Banner.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        Banner.SetResourceReference(StyleProperty, failed ? "Primer.Alert.Error" : "Primer.Alert.Success");
        BannerIcon.SetResourceReference(StyleProperty, failed ? "Primer.AlertIcon.Error" : "Primer.AlertIcon.Success");
        string severity = Loc.Current[failed ? "Audit.Error" : "Audit.Done"];
        string shown = text is null ? string.Empty : Loc.Current.T("Audit.Banner", severity, text);
        if (BannerText.Text != shown)
        {
            BannerText.Text = shown;
            AccessibleStatus.Announce(BannerText);
        }

        BannerDetails.Text = details ?? string.Empty;
        BannerDetails.Visibility = details is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Fill(ComboBox filter, IReadOnlyList<FilterOption> options, string? chosen)
    {
        _choosing = true;
        filter.ItemsSource = options;
        filter.SelectedItem = options.FirstOrDefault(o => o.Value == chosen) ?? options[0];
        _choosing = false;
    }

    private void OnFilter(object sender, SelectionChangedEventArgs e)
    {
        if (!_choosing && _view is { } view && sender is ComboBox { SelectedItem: FilterOption option } filter)
        {
            _ = filter.Name switch
            {
                nameof(DayFilter) => view.Day = option.Value,
                nameof(ToolFilter) => view.Tool = option.Value,
                nameof(ProjectFilter) => view.Project = option.Value,
                nameof(DecisionFilter) => view.Decision = option.Value,
                _ => view.Execution = option.Value,
            };
        }
    }

    private void OnReload(object sender, RoutedEventArgs e) => Refresh();

    private void OnVerify(object sender, RoutedEventArgs e) => _ = _view?.VerifyAsync(CancellationToken.None);

    private void OnFolder(object sender, RoutedEventArgs e) => OpenFolder();

    /// <summary>The audit folder in Explorer; when Windows cannot open it, the page says so where it was asked (STS-05).</summary>
    internal bool OpenFolder()
    {
        bool opened = _view is { } view && _shell.Open(view.Folder);
        Notice.Text = opened ? string.Empty : Loc.Current.T("Audit.FolderFailed", _view?.Folder ?? string.Empty);
        Notice.Visibility = opened ? Visibility.Collapsed : Visibility.Visible;
        if (!opened)
        {
            AccessibleStatus.Announce(Notice);
        }

        return opened;
    }

    private static void Explain(FrameworkElement element, string help)
    {
        element.ToolTip = help;
        AutomationProperties.SetHelpText(element, help);
    }
}
