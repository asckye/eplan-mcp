using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EplanMcp.Workbench.Approvals;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// The Workbench window (workbench-ui 3.1): Sider, Header and the pages. Every page change goes through
/// <see cref="Navigate"/> (NAV-02), so the focus rules live in one place.
/// </summary>
public partial class MainWindow : Window
{
    public static readonly RoutedUICommand NavigateCommand = new("Navigate", nameof(NavigateCommand), typeof(MainWindow));

    public static readonly RoutedUICommand CycleCommand = new("Cycle", nameof(CycleCommand), typeof(MainWindow));

    /// <summary>Below this window width the Sider shows icons only (3.1).</summary>
    private const double CompactBelow = 992;

    private readonly (WorkbenchPage Page, string Key, string Shortcut, RadioButton Item, FrameworkElement View)[] _pages;
    private bool? _compactChosen;
    private ApprovalChannelState _channel;
    private int _pending;
    private IInputElement? _settingsOpener;

    public MainWindow()
    {
        InitializeComponent();
        _pages =
        [
            (WorkbenchPage.Approvals, "Page.Approvals", "Ctrl+1", ApprovalsItem, ApprovalsPage),
            (WorkbenchPage.Audit, "Page.Audit", "Ctrl+2", AuditItem, AuditPage),
            (WorkbenchPage.Status, "Page.Status", "Ctrl+3", StatusItem, StatusPage),
            (WorkbenchPage.Clients, "Page.Clients", "Ctrl+4", ClientsItem, ClientsPage),
        ];
        VersionText.Text = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
        // The shortcuts reach no page while the settings drawer covers the window (DLG-10).
        CommandBindings.Add(new CommandBinding(NavigateCommand, (_, e) => Navigate((WorkbenchPage)e.Parameter, NavigationOrigin.Shortcut), CanNavigate));
        CommandBindings.Add(new CommandBinding(CycleCommand, (_, e) => Cycle(int.Parse((string)e.Parameter, CultureInfo.InvariantCulture)), CanNavigate));
        SizeChanged += (_, _) => ApplySider();
        Loc.Current.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.Current.LanguageChanged -= OnLanguageChanged;
        StateChanged += (_, _) => ApplyWindowState();
        Loaded += (_, _) => Navigate(WorkbenchPage.Approvals, NavigationOrigin.Startup);
        ApplyWindowState();
        Navigate(WorkbenchPage.Approvals, NavigationOrigin.Menu);
    }

    public WorkbenchPage CurrentPage { get; private set; }

    /// <summary>Closing then only hides the window to the tray, and the queue goes on (NAV-09, APR-15).</summary>
    public bool HideOnClose { get; set; }

    /// <summary>
    /// Shows <paramref name="page"/>. From a menu item focus stays on the item; from anywhere else it goes to the
    /// page's heading, never to a button (NAV-07).
    /// </summary>
    public void Navigate(WorkbenchPage page, NavigationOrigin origin)
    {
        CurrentPage = page;
        foreach ((WorkbenchPage each, _, _, RadioButton item, FrameworkElement view) in _pages)
        {
            item.IsChecked = each == page;
            view.Visibility = each == page ? Visibility.Visible : Visibility.Collapsed;
        }

        ApplyTexts();
        if (origin != NavigationOrigin.Menu && IsLoaded)
        {
            UpdateLayout();
            ((IWorkbenchPage)_pages[(int)page].View).FocusHeading();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (HideOnClose && !e.Cancel)
        {
            e.Cancel = true;
            Hide();
        }
    }

    /// <summary>
    /// Shows whether approvals are taken and how many questions wait (STS-10, STS-11); a change of state is announced
    /// (STS-02).
    /// </summary>
    public void ShowApprovals(ApprovalChannelState channel, int pending)
    {
        bool changed = channel != _channel;
        (_channel, _pending) = (channel, pending);
        ApplyStatus();
        if (changed)
        {
            AccessibleStatus.Announce(PipeText);
        }
    }

    /// <summary>Shows the window from the tray or for a second start, in front: the engineer asked for it (NAV-09).</summary>
    public void Restore()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Opens the settings drawer over a scrim, focus on its title; closing gives focus back (DLG-10).</summary>
    public void OpenSettings()
    {
        _settingsOpener = Keyboard.FocusedElement;
        SettingsLayer.Visibility = Visibility.Visible;
        UpdateLayout();
        SettingsPane.FocusHeading();
    }

    public void CloseSettings()
    {
        SettingsLayer.Visibility = Visibility.Collapsed;
        (_settingsOpener ?? SettingsButton).Focus();
        _settingsOpener = null;
    }

    private void CanNavigate(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = SettingsLayer.Visibility != Visibility.Visible;

    private void Cycle(int step) => Navigate((WorkbenchPage)(((int)CurrentPage + step + _pages.Length) % _pages.Length), NavigationOrigin.Shortcut);

    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OnPendingBadge(object sender, RoutedEventArgs e) => Navigate(WorkbenchPage.Approvals, NavigationOrigin.Reminder);

    private void ApplyStatus()
    {
        Loc loc = Loc.Current;
        (string text, string help, string icon) = _channel switch
        {
            ApprovalChannelState.Accepting => ("Pipe.Accepting", string.Empty, "Primer.AlertIcon.Success"),
            ApprovalChannelState.Full => ("Pipe.Full", "Pipe.FullHelp", "Primer.AlertIcon.Warning"),
            _ => ("Pipe.Taken", "Pipe.TakenHelp", "Primer.AlertIcon.Error"),
        };
        PipeText.Text = loc.T(text, ApprovalQueue.DefaultCapacity);
        PipeIcon.SetResourceReference(StyleProperty, icon);
        PipeText.ToolTip = help.Length == 0 ? PipeText.Text : PipeText.Text + Environment.NewLine + loc[help];
        AutomationProperties.SetHelpText(PipeText, help.Length == 0 ? string.Empty : loc[help]);
        string pending = loc.T("Tray.Pending", _pending);
        PendingBadge.Visibility = _pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingBadge.Content = _pending.ToString(CultureInfo.CurrentCulture);
        PendingBadge.ToolTip = pending;
        AutomationProperties.SetName(PendingBadge, pending);
    }

    private void OnSettingsClosed(object? sender, EventArgs e) => CloseSettings();

    private void OnScrim(object sender, MouseButtonEventArgs e) => CloseSettings();

    private void OnSiderToggle(object sender, RoutedEventArgs e)
    {
        _compactChosen = !Ux.GetCompact(Sider);
        ApplySider();
    }

    /// <summary>A choice by hand holds until the window crosses the breakpoint; then the width decides again (3.1).</summary>
    private void ApplySider()
    {
        bool narrow = ActualWidth > 0 && ActualWidth < CompactBelow;
        if (_compactChosen == narrow)
        {
            _compactChosen = null;
        }

        bool compact = _compactChosen ?? narrow;
        Ux.SetCompact(Sider, compact);
        Sider.Width = compact ? 56 : 216;
        Brand.Visibility = VersionChip.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Ux.SetIcon(SiderToggle, (Geometry)FindResource(compact ? "Icon.MenuUnfold" : "Icon.MenuFold"));
        ApplyTexts();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyTexts();

    private void OnMenuClick(object sender, RoutedEventArgs e) =>
        Navigate(_pages.Single(p => ReferenceEquals(p.Item, sender)).Page, NavigationOrigin.Menu);

    private void ApplyWindowState()
    {
        bool maximized = WindowState == WindowState.Maximized;

        // A maximized chrome-less window reaches past the screen by its resize border; keep the content on screen.
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeGlyph.Data = Geometry.Parse(maximized ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z" : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
        ApplyTexts();
    }

    /// <summary>Texts that code sets: names and tooltips that change with state, and the window title (NAV-03).</summary>
    private void ApplyTexts()
    {
        Loc loc = Loc.Current;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(loc.Language == AppLanguage.Chinese ? "zh-CN" : "en-US");
        string page = loc[_pages[(int)CurrentPage].Key];
        Title = loc.T("Shell.WindowTitle", page);
        CurrentPageText.Text = page;
        foreach ((_, string key, string shortcut, RadioButton item, _) in _pages)
        {
            item.ToolTip = loc.T("Shell.Shortcut", loc[key], shortcut);
        }

        string toggle = loc[Ux.GetCompact(Sider) ? "Shell.Expand" : "Shell.Collapse"];
        SiderToggle.Content = toggle;
        AutomationProperties.SetName(SiderToggle, toggle);
        ApplyStatus();
        Label(MinimizeButton, "Shell.Minimize");
        Label(MaximizeButton, WindowState == WindowState.Maximized ? "Shell.Restore" : "Shell.Maximize");
        Label(CloseButton, "Shell.Close");
        CloseButton.ToolTip = loc["Shell.CloseHint"];
        AutomationProperties.SetHelpText(CloseButton, loc["Shell.CloseHint"]);
    }

    private static void Label(ButtonBase button, string key)
    {
        AutomationProperties.SetName(button, Loc.Current[key]);
        button.ToolTip = Loc.Current[key];
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
