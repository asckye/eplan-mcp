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
    private List<AiCallRow> _allCalls = [];
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
            _view.Select(item);
        }
    }

    internal void FocusList()
    {
        UpdateLayout();
        // The selected row in whichever section holds it, else the first waiting row, else the first answered one.
        ListBox list = AnsweredList.SelectedItem is not null || Questions.Items.Count == 0 && AnsweredList.Items.Count > 0 ? AnsweredList : Questions;
        object? row = list.Items.Count == 0 ? null : list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem ?? list.Items[0]);
        Keyboard.Focus(row as IInputElement ?? list);
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
        Empty.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        NoPending.Visibility = _view?.Pending.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        // One selection across both sections: the shown question, in the section that holds it.
        Questions.SelectedItem = _view?.Shown is { IsPending: true } waiting ? waiting : null;
        AnsweredList.SelectedItem = _view?.Shown is { IsPending: false } answered ? answered : null;
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
        bool denied = _policy?.WritesDenied == true;
        PolicyBadgeText.Text = loc[denied ? "Approvals.PolicyDenied" : manual ? "Approvals.PolicyManual" : "Approvals.PolicyAuto"];
        PolicyToggleButton.Content = loc[manual ? "Approvals.ToggleToAuto" : "Approvals.ToggleToManual"];
        PolicyToggleButton.IsEnabled = !denied;
        ApprovalPolicyBar.Visibility = _policy is not null && ApprovalsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PolicyDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, manual && !denied ? "State.Success.Text" : "State.Warning.Text");
    }

    private void ApplyButtons()
    {
        RejectButton.IsEnabled = ApproveButton.IsEnabled = _view?.CanAnswer == true;
        QuestionItem? shown = _view?.Shown;
        bool destructive = shown?.IsDestructive == true;
        // A Write keeps two equal buttons (ADR-0014). The safe answer is primary for a Destructive write (ADR-0035)
        // and for a release (ADR-0010); the Destructive action is the danger button.
        string reject = destructive || shown?.IsRelease == true ? "Workbench.AnswerPrimary" : "Workbench.Answer";
        string approve = destructive ? "Workbench.AnswerDanger" : "Workbench.Answer";
        if (!ReferenceEquals(RejectButton.Style, FindResource(reject)))
        {
            RejectButton.Style = (Style)FindResource(reject);
        }

        if (!ReferenceEquals(ApproveButton.Style, FindResource(approve)))
        {
            ApproveButton.Style = (Style)FindResource(approve);
        }

        KindTag.Style = (Style)FindResource(destructive ? "Workbench.Tag.Error" : shown?.IsRelease == true ? "Workbench.Tag.Warning" : "Workbench.Tag.Accent");
        StatusTag.Style = (Style)FindResource(shown?.IsPending == true ? "Workbench.Tag.Warning" : "Workbench.Tag");
    }

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
            ApprovalPolicyUi.Set(policy, !policy.RequireNormalWriteApproval, this);
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
        ApplyTexts();
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
        ShowCall(visible.Length > 0 ? visible[0] : null);
    }

    private void OnCallsFilterChanged(object sender, RoutedEventArgs e) => ApplyCallsFilter();

    private void OnCallsReload(object sender, RoutedEventArgs e) => ReloadCalls();

    /// <summary>The inspector shows one record: its summary and the text as the log has it; nothing without one.</summary>
    private void ShowCall(AiCallRow? row)
    {
        CallSummary.DataContext = row;
        CallInspectorText.Text = row?.RawDetail ?? string.Empty;
    }

    private void OnInspectorToggle(object sender, RoutedEventArgs e)
    {
        bool shown = InspectorToggle.IsChecked == true;
        CallInspector.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        InspectorColumn.Width = shown ? new GridLength(344) : new GridLength(0);
    }

    private void OnCallSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CallsList.SelectedItem is AiCallRow row)
        {
            ShowCall(row);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_view is not null && sender is ListBox { SelectedItem: QuestionItem item } && item != _view.Shown)
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

    /// <summary>Asks for the typed confirmation of a Destructive write (ADR-0035); a test replaces it.</summary>
    internal Func<QuestionItem, bool>? ConfirmDestructive { get; set; }

    private void OnApprove(object sender, RoutedEventArgs e)
    {
        if (_view is not { CanAnswer: true, Shown: { } shown })
        {
            return;
        }

        // The second step of a Destructive approval. The countdown keeps running behind the dialog, and the answer
        // below is refused if the question ended meanwhile.
        if (shown.IsDestructive && !(ConfirmDestructive ?? (item => DestructiveConfirmDialog.Confirm(Window.GetWindow(this)!, item)))(shown))
        {
            return;
        }

        if (ReferenceEquals(_view.Shown, shown))
        {
            Answer(approve: true);
        }
    }

    /// <summary>
    /// ADR-0035: on a Destructive question Enter answers Reject, the one key that answers and only towards safety. A
    /// button that has the focus handles Enter itself, so this is reached from the heading and the read-only texts.
    /// </summary>
    private void OnDetailKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && _view is { CanAnswer: true, Shown.IsDestructive: true })
        {
            e.Handled = true;
            Answer(approve: false);
        }
    }

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
        // Side by side, the detail fills the right of the queue pane; in a narrow window it goes below the list.
        bool wide = ActualWidth == 0 || ActualWidth >= SideBySide;
        NarrowDetailRow.Height = wide || DetailArea.Visibility != Visibility.Visible ? GridLength.Auto : new GridLength(2, GridUnitType.Star);
        Grid.SetColumnSpan(Questions, wide ? 1 : 2);
        Grid.SetColumnSpan(QueuePane, wide ? 1 : 2);
        Grid.SetRow(DetailArea, wide ? 0 : 2);
        Grid.SetRowSpan(DetailArea, wide ? 2 : 1);
        Grid.SetColumn(DetailArea, wide ? 1 : 0);
        Grid.SetColumnSpan(DetailArea, wide ? 1 : 2);
    }
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyTexts();
        ApplyPolicy();
    }

    private void ApplyTexts()
    {
        Loc loc = Loc.Current;
        int waiting = _view?.Pending.Count ?? 0;
        int answered = _view?.Answered.Count ?? 0;
        WaitingCount.Text = waiting.ToString(CultureInfo.CurrentCulture);
        AnsweredCount.Text = answered.ToString(CultureInfo.CurrentCulture);
        AutomationProperties.SetName(Questions, loc.T("Approvals.Waiting", waiting));
        AutomationProperties.SetName(AnsweredList, loc.T("Approvals.Answered", answered));
        PendingCountText.Text = waiting.ToString(CultureInfo.CurrentCulture);
        PendingCountPill.Visibility = waiting > 0 ? Visibility.Visible : Visibility.Collapsed;
        CallsCountText.Text = _allCalls.Count.ToString(CultureInfo.CurrentCulture);
        CallTimeCol.Header = loc["Audit.Time"];
        CallToolCol.Header = loc["Audit.Tool"];
        CallKindCol.Header = loc["Audit.Kind"];
        CallProjectCol.Header = loc["Audit.Project"];
        CallResultCol.Header = loc["Audit.Result"];
    }
}
