using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EplanMcp.Workbench.Audit;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Approvals;

internal sealed record AiCallRow(string Time, string Tool, string Kind, string Project, string Result, bool IsWrite, bool IsFailed, string RawDetail);

/// <summary>
/// The approval queue and AI calls page over <see cref="ApprovalQueueView"/>.
/// Preserves ADR-0014 focus, equal-weight buttons, and 500 ms guard invariants.
/// </summary>
public partial class ApprovalsPage : UserControl, IWorkbenchPage
{
    private const double SideBySide = 760;

    private readonly DispatcherTimer _ticks;
    private ApprovalQueueView? _view;
    private SafetyPolicyStore? _policy;
    private string? _auditFolder;
    private IReadOnlyList<AiCallRow> _allCalls = [];
    private bool _focusOnShow;

    public ApprovalsPage()
    {
        InitializeComponent();
        _ticks = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => _view?.Tick(), Dispatcher);
        SizeChanged += (_, _) => Arrange();

        Loaded += (_, _) =>
        {
            Loc.Current.LanguageChanged -= OnLanguageChanged;
            Loc.Current.LanguageChanged += OnLanguageChanged;
            ApplyTexts();
            _ticks.Start();
        };
        Unloaded += (_, _) =>
        {
            Loc.Current.LanguageChanged -= OnLanguageChanged;
            _ticks.Stop();
        };
        ApplyTexts();
        Show();
    }

    /// <summary>Where focus goes when this page opens: the shown question's title, else the page title (NAV-07).</summary>
    public FrameworkElement? Heading => ApprovalsTab.IsChecked == true && _view?.Shown is not null ? QuestionTitle : PageTitle;

    /// <summary>Whether keyboard focus is on one of the two answer buttons.</summary>
    internal bool HasAnswerFocus => RejectButton.IsKeyboardFocused || ApproveButton.IsKeyboardFocused;

    internal ApprovalQueueView? View
    {
        get => _view;
        set
        {
            if (_view is not null)
            {
                _view.PropertyChanged -= OnViewChanged;
                _view.ShownChanged -= OnShownChanged;
                _view.Announced -= OnAnnounced;
            }

            _view = value;
            DataContext = value;
            if (value is not null)
            {
                value.PropertyChanged += OnViewChanged;
                value.ShownChanged += OnShownChanged;
                value.Announced += OnAnnounced;
            }

            Show();
        }
    }

    internal void AttachPolicy(SafetyPolicyStore policy, string? auditFolder = null)
    {
        _policy = policy;
        _auditFolder = auditFolder;
        _policy.Changed += () => Dispatcher.BeginInvoke(ApplyPolicy);
        ApplyPolicy();
    }

    public void FocusHeading()
    {
        UpdateLayout();
        if (Heading is { } heading)
        {
            Keyboard.Focus(heading);
        }
    }

    internal void Open(QuestionItem item)
    {
        ApprovalsTab.IsChecked = true;
        if (_view is not null && (_view.Pending.Contains(item) || _view.Answered.Contains(item)))
        {
            (item.IsPending ? WaitingGroup : AnsweredGroup).IsChecked = true;
            _view.Select(item);
        }
    }

    internal void FocusList()
    {
        UpdateLayout();
        object? row = Questions.Items.Count == 0 ? null : Questions.ItemContainerGenerator.ContainerFromItem(Questions.SelectedItem ?? Questions.Items[0]);
        Keyboard.Focus(row as IInputElement ?? (WaitingGroup.IsChecked == true ? WaitingGroup : AnsweredGroup));
    }

    internal void Rearm()
    {
        _view?.Guard.Arm();
        Rendered();
    }

    private void Show()
    {
        bool shown = _view?.Shown is not null;
        DetailArea.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = _view?.IsEmpty != false && WaitingGroup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        Questions.SelectedItem = _view?.Shown;
        ApplyButtons();
        ApplyTexts();
        ApplyPolicy();
        Arrange();

        if (_view?.Shown is { IsPending: false } && HasAnswerFocus)
        {
            Keyboard.Focus(QuestionTitle);
        }
    }

    private void ApplyPolicy()
    {
        bool manual = _policy?.RequireNormalWriteApproval ?? true;
        Loc loc = Loc.Current;
        PolicyBadgeText.Text = loc[manual ? "Approvals.PolicyManual" : "Approvals.PolicyAuto"];
        PolicyToggleButton.Content = loc[manual ? "Approvals.ToggleToAuto" : "Approvals.ToggleToManual"];
        ApprovalPolicyBar.Visibility = _policy is not null && ApprovalsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyButtons() => RejectButton.IsEnabled = ApproveButton.IsEnabled = _view?.CanAnswer == true;

    private void OnViewChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.PropertyName == nameof(ApprovalQueueView.CanAnswer))
        {
            ApplyButtons();
        }
        else
        {
            Show();
        }
    });

    private void OnShownChanged()
    {
        Show();
        Rendered();
        if (_focusOnShow)
        {
            _focusOnShow = false;
            Dispatcher.BeginInvoke(FocusHeading, DispatcherPriority.Input);
        }
    }

    private void Rendered() => Dispatcher.BeginInvoke(() => _view?.Guard.Rendered(), DispatcherPriority.ContextIdle);

    private void OnAnnounced(string text)
    {
        Announcer.Text = text;
        AccessibleStatus.Announce(Announcer);
    }

    private void OnGroup(object sender, RoutedEventArgs e)
    {
        if (_view is null || Questions is null)
        {
            return;
        }

        Questions.ItemsSource = AnsweredGroup.IsChecked == true ? _view.Answered : _view.Pending;
        AutomationProperties.SetName(Questions, Loc.Current[AnsweredGroup.IsChecked == true ? "Approvals.AnsweredList" : "Approvals.List"]);
        Show();
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (Layout is null || CallsPanel is null)
        {
            return;
        }

        bool approvals = ApprovalsTab.IsChecked == true;
        Layout.Visibility = approvals ? Visibility.Visible : Visibility.Collapsed;
        CallsPanel.Visibility = approvals ? Visibility.Collapsed : Visibility.Visible;
        ApplyPolicy();
        if (!approvals)
        {
            ReloadCalls();
        }
    }

    private void OnPolicyToggle(object sender, RoutedEventArgs e)
    {
        if (_policy is { } policy)
        {
            policy.SetRequireNormalWriteApproval(!policy.RequireNormalWriteApproval);
        }
    }

    private void ReloadCalls()
    {
        var list = new List<AiCallRow>();
        if (_auditFolder is { Length: > 0 } folder)
        {
            try
            {
                AuditLogContents read = AuditReader.Read(folder, progress: null, CancellationToken.None);
                foreach (AuditRecord r in read.Records.TakeLast(120).Reverse())
                {
                    string result = ProjectText.Line(r.Execution ?? r.Decision ?? r.Kind ?? "-");
                    bool failed = r.Decision is "denied" or "expired" or "cancelled" || r.Execution is "unknown" or "rolled_back";
                    bool isWrite = r.Kind is "execution_started" or "execution_result" or "approval";
                    string detail = string.Join(Environment.NewLine,
                        $"time: {r.Time?.ToString("O", CultureInfo.InvariantCulture) ?? "-"}",
                        $"kind: {ProjectText.Line(r.Kind ?? "-")}",
                        $"tool: {ProjectText.Line(r.Tool ?? "-")}",
                        $"project: {ProjectText.Line(r.Project ?? "-")}",
                        $"decision: {ProjectText.Line(r.Decision ?? "-")}",
                        $"execution: {ProjectText.Line(r.Execution ?? "-")}",
                        $"file: {ProjectText.Line(r.File)}:{r.Line}");
                    list.Add(new AiCallRow(
                        r.Time?.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture) ?? "--",
                        ProjectText.Line(r.Tool ?? "-"),
                        ProjectText.Line(r.Kind ?? "-"),
                        ProjectText.Line(string.IsNullOrWhiteSpace(r.Project) ? "-" : r.Project),
                        result,
                        isWrite,
                        failed,
                        detail));
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Keep empty list if audit folder is missing or unreadable.
            }
        }

        _allCalls = list;
        ApplyCallsFilter();
    }

    private void ApplyCallsFilter()
    {
        if (CallsList is null || CallsSearch is null)
        {
            return;
        }

        string q = CallsSearch.Text.Trim();
        IEnumerable<AiCallRow> rows = _allCalls;
        if (CallsWrite.IsChecked == true)
        {
            rows = rows.Where(c => c.IsWrite);
        }
        else if (CallsFailed.IsChecked == true)
        {
            rows = rows.Where(c => c.IsFailed);
        }

        if (q.Length > 0)
        {
            rows = rows.Where(c => c.Tool.Contains(q, StringComparison.OrdinalIgnoreCase)
                || c.Project.Contains(q, StringComparison.OrdinalIgnoreCase)
                || c.Result.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        AiCallRow[] visible = [.. rows];
        CallsList.ItemsSource = visible;
        CallsEmpty.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CallInspectorText.Text = visible.Length > 0 ? visible[0].RawDetail : string.Empty;
    }

    private void OnCallsFilterChanged(object sender, RoutedEventArgs e) => ApplyCallsFilter();

    private void OnCallsReload(object sender, RoutedEventArgs e) => ReloadCalls();

    private void OnCallSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CallsList.SelectedItem is AiCallRow row)
        {
            CallInspectorText.Text = row.RawDetail;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_view is not null && Questions.SelectedItem is QuestionItem item && item != _view.Shown)
        {
            _view.Select(item);
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            FocusHeading();
        }
    }

    private void OnListOpen(object sender, MouseButtonEventArgs e) => FocusHeading();

    private void OnReject(object sender, RoutedEventArgs e) => Answer(approve: false);

    private void OnApprove(object sender, RoutedEventArgs e) => Answer(approve: true);

    private void Answer(bool approve)
    {
        _focusOnShow = true;
        if (_view?.Answer(approve) != true)
        {
            _focusOnShow = false;
        }
    }

    private void Arrange()
    {
        bool wide = ActualWidth == 0 || ActualWidth >= SideBySide;
        NarrowDetailRow.Height = wide || DetailArea.Visibility != Visibility.Visible ? GridLength.Auto : new GridLength(2, GridUnitType.Star);
        Grid.SetColumnSpan(Questions, wide ? 1 : 2);
        Grid.SetRow(DetailArea, wide ? 1 : 2);
        Grid.SetColumn(DetailArea, wide ? 1 : 0);
        Grid.SetColumnSpan(DetailArea, wide ? 1 : 2);
        DetailArea.Margin = wide ? (Thickness)FindResource("Ui.Gap.Left.LG") : (Thickness)FindResource("Ui.Gap.Top.LG");
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyTexts();
        ApplyPolicy();
    }

    private void ApplyTexts()
    {
        Loc loc = Loc.Current;
        WaitingGroup.Content = loc.T("Approvals.Waiting", _view?.Pending.Count ?? 0);
        AnsweredGroup.Content = loc.T("Approvals.Answered", _view?.Answered.Count ?? 0);
        AutomationProperties.SetName(WaitingGroup, (string)WaitingGroup.Content);
        AutomationProperties.SetName(AnsweredGroup, (string)AnsweredGroup.Content);
        CallTimeCol.Header = loc["Audit.Time"];
        CallToolCol.Header = loc["Audit.Tool"];
        CallKindCol.Header = loc["Audit.Kind"];
        CallProjectCol.Header = loc["Audit.Project"];
        CallResultCol.Header = loc["Audit.Result"];
    }
}
