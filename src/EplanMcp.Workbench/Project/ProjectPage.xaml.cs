using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Project;

/// <summary>
/// The project workspace (Workbench 4.0 design, ADR-0065): reads when first shown and on F5, never on its own; a chosen
/// page or group reads its devices. Every text from EPLAN is made plain in the model.
/// </summary>
public partial class ProjectPage : UserControl, IWorkbenchPage
{
    private ProjectView? _view;
    private bool _loadedOnce;
    private ProjectTreeNode? _chosen;
    private IReadOnlyList<ProjectGroup>? _shownGroups;

    public ProjectPage()
    {
        InitializeComponent();
        Confirm = (title, message, details) => WorkbenchDialog.Confirm(
            Window.GetWindow(this)!, title, message, Loc.Current["Confirm.Cancel"], Loc.Current["Project.ConfirmRun"], details);
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

    public FrameworkElement? Heading => PageTitle;

    /// <summary>
    /// The engineer's confirmation of a write: title, message and the change list; true only for "run" (DLG-12). Tests
    /// replace it.
    /// </summary>
    internal Func<string, string, string, bool> Confirm { get; set; }

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    internal void SetLeftPaneVisible(bool visible)
    {
        LeftPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LeftCol.Width = visible ? new GridLength(262) : new GridLength(0);
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
        _shownGroups = null;
        Show();
    }

    private void Show()
    {
        Loc loc = Loc.Current;
        if (_view is not { } view)
        {
            ShowEmpty(loc["Project.NotConnected"], string.Empty);
            return;
        }

        Working.Visibility = view.IsLoading || view.DevicesLoading || view.Acting ? Visibility.Visible : Visibility.Collapsed;
        bool ready = view.State == ProjectState.Ready && !view.Acting;
        ExportButton.IsEnabled = ReportsButton.IsEnabled = CheckButton.IsEnabled = ready;
        CheckButton.Content = _chosen?.Page is not null ? loc["Project.CheckPage"] : loc["Project.CheckRun"];
        ActionStrip.Visibility = view.ActionResult.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ActionText.Text = view.ActionResult;
        ActionText.Foreground = (System.Windows.Media.Brush)FindResource(view.ActionFailed ? "State.Error.Text" : "Text.Primary");
        ActionStrip.Background = (System.Windows.Media.Brush)FindResource(view.ActionFailed ? "State.Error.Background" : "State.Success.Background");
        Narrow();
        RefreshButton.IsEnabled = EmptyRefresh.IsEnabled = view.State != ProjectState.NoHost && !view.IsLoading;
        CountsText.Text = view.Summary + (view.Truncated ? loc.T("Project.Truncated", ProjectView.MaxPages) : string.Empty);
        HeaderEplan.Text = view.Eplan;
        if (!ReferenceEquals(_shownGroups, view.Groups))
        {
            _shownGroups = view.Groups;
            Tree.ItemsSource = ProjectTreeNode.From(view.Groups, view.ProjectName, TreeSearch.Text.Trim());
        }

        switch (view.State)
        {
            case ProjectState.Ready when _chosen is not null:
                ShowDetail(view);
                break;
            case ProjectState.Ready:
                ShowEmpty(view.ProjectName, loc["Project.ChooseNode"]);
                break;
            case ProjectState.NoHost:
                ShowEmpty(loc["Host.Missing"], string.Empty);
                break;
            case ProjectState.NotConnected:
                ShowEmpty(loc["Project.NotConnected"], view.Problem.Length > 0 ? view.Problem : loc["Project.NotConnectedHelp"]);
                break;
            case ProjectState.Failed:
                ShowEmpty(loc["Project.ReadFailed"], view.Problem);
                break;
            default:
                ShowEmpty(loc["Project.Reading"], string.Empty);
                break;
        }
    }

    private void ShowEmpty(string title, string text)
    {
        Detail.Visibility = Visibility.Collapsed;
        Empty.Visibility = Visibility.Visible;
        EmptyState.Title = title;
        EmptyState.Text = text;
        HeaderBadge.Visibility = Visibility.Collapsed;
        HeaderTitle.Text = _view?.ProjectName ?? string.Empty;
    }

    private void ShowDetail(ProjectView view)
    {
        Loc loc = Loc.Current;
        Detail.Visibility = Visibility.Visible;
        Empty.Visibility = Visibility.Collapsed;
        ProjectPageNode? page = _chosen!.Page;
        HeaderBadge.Visibility = page is { Kind.Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        HeaderBadgeText.Text = page?.Kind ?? string.Empty;
        HeaderTitle.Text = page?.Name ?? (_chosen.DevicesPrefix is { Length: > 0 } prefix ? prefix : view.ProjectName);
        PageTabs.Visibility = page is not null ? Visibility.Visible : Visibility.Collapsed;
        HeaderEplan.Visibility = page is null ? Visibility.Visible : Visibility.Collapsed;
        int flagged = view.CheckErrors + view.CheckWarnings;
        ReviewCount.Text = flagged.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ReviewCountPill.Visibility = flagged > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool netlist = page is not null && TabSchematic.IsChecked == true;
        bool review = page is not null && TabReview.IsChecked == true;
        NetlistPanel.Visibility = netlist ? Visibility.Visible : Visibility.Collapsed;
        ReviewPanel.Visibility = review ? Visibility.Visible : Visibility.Collapsed;
        DevicesPanel.Visibility = !netlist && !review ? Visibility.Visible : Visibility.Collapsed;
        string lines = string.Join(Environment.NewLine, view.Netlist);
        if (NetlistText.Text != lines)
        {
            NetlistText.Text = lines;
        }

        NoNetlist.Visibility = !view.DevicesLoading && view.Netlist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewSummary.Text = loc.T("Project.ReviewSummary", view.CheckErrors, view.CheckWarnings);
        Checks.ItemsSource = view.Checks;
        NoChecks.Visibility = !view.DevicesLoading && view.Checks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailTitle.Text = page is not null ? (page.Description.Length > 0 ? page.Description : page.Name) : loc["Project.DeviceList"];
        DetailSub.Text = page is not null ? page.Type : loc.T("Project.GroupDevices", HeaderTitle.Text);
        Devices.ItemsSource = view.Devices;
        NoDevices.Visibility = !view.DevicesLoading && view.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not ProjectTreeNode { } node || (node.Page is null && node.DevicesPrefix is null) || _view is not { } view)
        {
            return;
        }

        _chosen = node;
        _ = view.LoadDevicesAsync(node.Page?.Name, node.DevicesPrefix, CancellationToken.None);
        Show();
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        if (_view is { } view)
        {
            Tree.ItemsSource = ProjectTreeNode.From(view.Groups, view.ProjectName, TreeSearch.Text.Trim());
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    /// <summary>The Workbench 4.0 design's narrow layout: a chosen page hides its name and the counts so its tabs and buttons fit.</summary>
    private const double NarrowWorkspace = 900;

    private void OnWorkspaceSized(object sender, SizeChangedEventArgs e) => Narrow();

    private void Narrow()
    {
        bool page = _chosen?.Page is not null;
        bool narrow = Workspace.ActualWidth is > 0 and < NarrowWorkspace;
        CountsText.Visibility = page ? Visibility.Collapsed : Visibility.Visible;
        HeaderTitle.Visibility = page && narrow ? Visibility.Collapsed : Visibility.Visible;
        if (page && narrow)
        {
            HeaderBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        ContextMenu menu = ExportButton.ContextMenu;
        menu.PlacementTarget = ExportButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnExportPdf(object sender, RoutedEventArgs e) => Export(dxf: false);

    private void OnExportDxf(object sender, RoutedEventArgs e) => Export(dxf: true);

    internal void Export(bool dxf)
    {
        if (_view is { } view)
        {
            _ = view.ExportAsync(EngineerCalls.Export(view.ProjectName, _chosen?.Page?.Name, dxf), CancellationToken.None);
        }
    }

    private void OnReports(object sender, RoutedEventArgs e)
    {
        if (_view is { } view)
        {
            _ = view.WriteAsync(EngineerCalls.ReportUpdate(view.ProjectName), changes => Ask("Project.ConfirmReports", changes), CancellationToken.None);
        }
    }

    private void OnCheck(object sender, RoutedEventArgs e) => _ = CheckAsync();

    /// <summary>Checks the chosen page, or the project when none is chosen, then reads the page's messages again.</summary>
    internal async Task CheckAsync()
    {
        if (_view is not { } view)
        {
            return;
        }

        ProjectTreeNode? chosen = _chosen;
        await view.WriteAsync(EngineerCalls.CheckRun(view.ProjectName, chosen?.Page?.Name), changes => Ask("Project.ConfirmCheck", changes), CancellationToken.None).ConfigureAwait(true);
        if (chosen is not null && ReferenceEquals(chosen, _chosen))
        {
            await view.LoadDevicesAsync(chosen.Page?.Name, chosen.DevicesPrefix, CancellationToken.None).ConfigureAwait(true); // the check replaced the messages
        }
    }

    private bool Ask(string title, IReadOnlyList<string> changes) =>
        Confirm(Loc.Current[title], Loc.Current.T("Project.ConfirmMessage", changes.Count), string.Join(Environment.NewLine, changes));

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            Show();
        }
    }
}
