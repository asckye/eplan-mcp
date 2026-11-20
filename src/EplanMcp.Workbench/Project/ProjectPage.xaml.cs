using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Project;

public partial class ProjectPage : UserControl, IWorkbenchPage
{
    private ProjectView? _view;
    private bool _loadedOnce;

    public ProjectPage()
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

    public event Action<WorkbenchPage>? NavigateRequested;

    public FrameworkElement? Heading => PageTitle;

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    internal void SetLeftPaneVisible(bool visible)
    {
        LeftPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LeftCol.Width = visible ? new GridLength(256) : new GridLength(0);
    }

    internal void Attach(ProjectView view)
    {
        _view = view;
        view.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Show);
        Show();
    }

    internal void Refresh()
    {
        if (_view is { IsLoading: false })
        {
            _ = _view.RefreshAsync(CancellationToken.None);
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
        ColTime.Header = ColTime2.Header = loc["Audit.Time"];
        ColTool.Header = ColTool2.Header = loc["Audit.Tool"];
        ColProject.Header = ColProject2.Header = loc["Audit.Project"];
        ColResult.Header = ColResult2.Header = loc["Audit.Result"];
        RecentHeading.Text = loc.T("Project.RecentCount", _view?.RecentActivity.Count ?? 0);
    }

    private void Show()
    {
        if (_view is not { } view)
        {
            SessionSummaryText.Text = Loc.Current["Project.NoSession"];
            NoSessionHelp.Visibility = Visibility.Visible;
            return;
        }

        Working.Visibility = view.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        SessionSummaryText.Text = string.IsNullOrWhiteSpace(view.SessionSummary) ? Loc.Current["Project.NoSession"] : view.SessionSummary;
        NoSessionHelp.Visibility = view.HasRunningSession ? Visibility.Collapsed : Visibility.Visible;
        SessionTree.ItemsSource = view.Sessions;
        SessionDetailList.ItemsSource = view.Sessions;
        OverviewActivityList.ItemsSource = view.RecentActivity;
        FullActivityList.ItemsSource = view.RecentActivity;
        RecentHeading.Text = Loc.Current.T("Project.RecentCount", view.RecentActivity.Count);
    }

    private void OnNodeChanged(object sender, RoutedEventArgs e)
    {
        if (OverviewPanel is null)
        {
            return;
        }

        OverviewPanel.Visibility = NodeOverview.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        EnvPanel.Visibility = NodeEnv.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.Visibility = NodeActivity.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnJumpApprovals(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(WorkbenchPage.Approvals);

    private void OnJumpMcp(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(WorkbenchPage.Mcp);
}
