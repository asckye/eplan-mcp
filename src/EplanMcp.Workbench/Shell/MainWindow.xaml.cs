using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// The Workbench window (workbench-ui 3.1): Sider, Header and the pages. Every page change goes through
/// <see cref="Navigate"/> (NAV-02), so the focus rules live in one place.
/// </summary>
public partial class MainWindow : Window
{
    public static readonly RoutedUICommand NavigateCommand = new("Navigate", nameof(NavigateCommand), typeof(MainWindow));

    private readonly (WorkbenchPage Page, string Key, string Shortcut, RadioButton Item, FrameworkElement View)[] _pages;

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
        CommandBindings.Add(new CommandBinding(NavigateCommand, (_, e) => Navigate((WorkbenchPage)e.Parameter, NavigationOrigin.Shortcut)));
        Loc.Current.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.Current.LanguageChanged -= OnLanguageChanged;
        StateChanged += (_, _) => ApplyWindowState();
        Loaded += (_, _) => Navigate(WorkbenchPage.Approvals, NavigationOrigin.Startup);
        ApplyWindowState();
        Navigate(WorkbenchPage.Approvals, NavigationOrigin.Menu);
    }

    public WorkbenchPage CurrentPage { get; private set; }

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

        Label(MinimizeButton, "Shell.Minimize");
        Label(MaximizeButton, WindowState == WindowState.Maximized ? "Shell.Restore" : "Shell.Maximize");
        Label(CloseButton, "Shell.Close");
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
