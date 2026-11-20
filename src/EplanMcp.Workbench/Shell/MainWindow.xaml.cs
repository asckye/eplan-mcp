using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Audit;
using EplanMcp.Workbench.Clients;
using EplanMcp.Workbench.Logs;
using EplanMcp.Workbench.Project;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Status;
using EplanMcp.Workbench.Themes;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// The Automation Studio 4.0 Native Desktop IDE Workbench window: 40 px titlebar, 60 px far-left Activity Rail,
/// 8 dedicated pages (Project, Approvals, MCP Server, AI Clients, Checks, Audit, Logs, Settings), 26 px status bar,
/// and Ctrl+K command palette. Every page change goes through <see cref="Navigate"/> (NAV-02).
/// </summary>
public partial class MainWindow : Window
{
    public static readonly RoutedUICommand NavigateCommand = new("Navigate", nameof(NavigateCommand), typeof(MainWindow));

    public static readonly RoutedUICommand CycleCommand = new("Cycle", nameof(CycleCommand), typeof(MainWindow));

    public static readonly RoutedUICommand RegionCommand = new("Region", nameof(RegionCommand), typeof(MainWindow));

    /// <summary>F5: refreshes the current page (KEY-07).</summary>
    public static readonly RoutedUICommand RefreshCommand = new("Refresh", nameof(RefreshCommand), typeof(MainWindow));

    /// <summary>Ctrl+F: opens the audit page and focuses its filters (KEY-07).</summary>
    public static readonly RoutedUICommand FindCommand = new("Find", nameof(FindCommand), typeof(MainWindow));

    /// <summary>Ctrl+K: opens the quick command palette.</summary>
    public static readonly RoutedUICommand CommandPaletteCommand = new("CommandPalette", nameof(CommandPaletteCommand), typeof(MainWindow));

    /// <summary>Ctrl+B: toggles secondary left tree/nav panes.</summary>
    public static readonly RoutedUICommand TogglePaneCommand = new("TogglePane", nameof(TogglePaneCommand), typeof(MainWindow));

    private sealed record PaletteEntry(string Label, Action Execute)
    {
        public override string ToString() => Label;
    }

    private readonly (WorkbenchPage Page, string Key, string Shortcut, RadioButton Item, FrameworkElement View)[] _pages;
    private ApprovalChannelState _channel;
    private int _pending;
    private bool _leftPaneOpen = true;
    private SafetyPolicyStore? _policy;
    private IInputElement? _recordOpener;
    private IInputElement? _commandOpener;
    private WindowState _lastState = WindowState.Normal;
    private PaletteEntry[] _paletteAll = [];

    public MainWindow()
    {
        InitializeComponent();
        _pages =
        [
            (WorkbenchPage.Project, "Page.Project", "Ctrl+1", ProjectItem, ProjectPage),
            (WorkbenchPage.Approvals, "Page.Approvals", "Ctrl+2", ApprovalsItem, ApprovalsPage),
            (WorkbenchPage.Mcp, "Page.Mcp", "Ctrl+3", McpItem, McpPage),
            (WorkbenchPage.Clients, "Page.Clients", "Ctrl+4", ClientsItem, ClientsPage),
            (WorkbenchPage.Status, "Page.Status", "Ctrl+5", StatusItem, StatusPage),
            (WorkbenchPage.Audit, "Page.Audit", "Ctrl+6", AuditItem, AuditPage),
            (WorkbenchPage.Logs, "Page.Logs", "Ctrl+7", LogsItem, LogsPage),
            (WorkbenchPage.Settings, "Page.Settings", "Ctrl+,", SettingsItem, SettingsPage),
        ];

        string version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.8.0";
        Brand.ToolTip = "EplanMcp " + version;
        VersionStatusText.Text = "v" + version;
        CommandKeyText.Text = "Ctrl+K";

        ProjectPage.NavigateRequested += p => Navigate(p, NavigationOrigin.Shortcut);
        McpPage.NavigateRequested += p => Navigate(p, NavigationOrigin.Shortcut);
        ClientsPage.NavigateRequested += p => Navigate(p, NavigationOrigin.Shortcut);
        StatusPage.ExportDiagnosticsRequested += () =>
        {
            Navigate(WorkbenchPage.Logs, NavigationOrigin.Shortcut);
            LogsPage.ExportDiagnostics();
        };
        LogsPage.StorageSettingsRequested += () =>
        {
            Navigate(WorkbenchPage.Settings, NavigationOrigin.Shortcut);
            SettingsPage.OpenStorageCategory();
        };

        CommandBindings.Add(new CommandBinding(NavigateCommand, (_, e) => Navigate((WorkbenchPage)e.Parameter, NavigationOrigin.Shortcut), CanNavigate));
        CommandBindings.Add(new CommandBinding(CycleCommand, (_, e) => Cycle(int.Parse((string)e.Parameter, CultureInfo.InvariantCulture)), CanNavigate));
        CommandBindings.Add(new CommandBinding(RegionCommand, (_, e) => MoveRegion(int.Parse((string)e.Parameter, CultureInfo.InvariantCulture)), CanNavigate));
        CommandBindings.Add(new CommandBinding(RefreshCommand, (_, _) => Refresh(), CanNavigate));
        CommandBindings.Add(new CommandBinding(FindCommand, (_, _) => Find(), CanNavigate));
        CommandBindings.Add(new CommandBinding(CommandPaletteCommand, (_, _) => OpenCommandPalette(), CanNavigate));
        CommandBindings.Add(new CommandBinding(TogglePaneCommand, (_, _) => ToggleLeftPane(), CanNavigate));

        Loc.Current.LanguageChanged += OnLanguageChanged;
        ThemeManager.Current.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(ApplyTexts);
        Closed += (_, _) => Loc.Current.LanguageChanged -= OnLanguageChanged;
        StateChanged += (_, _) => ApplyWindowState();
        SourceInitialized += (_, _) => WindowShape.RoundCorners(this);

        // APR-03, APR-04: when the window comes back, the buttons wait again and focus is on the question, not a button.
        IsVisibleChanged += (_, e) => Rearm(e.NewValue is true);
        Activated += (_, _) => Dispatcher.BeginInvoke(() => Refocus(), System.Windows.Threading.DispatcherPriority.Input);
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
        bool changed = CurrentPage != page;
        CurrentPage = page;
        foreach ((WorkbenchPage each, _, _, RadioButton item, FrameworkElement view) in _pages)
        {
            item.IsChecked = each == page;
            view.Visibility = each == page ? Visibility.Visible : Visibility.Collapsed;
        }

        if (changed && IsLoaded)
        {
            WindowShape.Enter(_pages[(int)page].View);
        }

        if (page == WorkbenchPage.Settings)
        {
            SettingsPage.Refresh();
        }

        ApplyTexts();
        Rearm(page == WorkbenchPage.Approvals);
        if (origin != NavigationOrigin.Menu && IsLoaded)
        {
            UpdateLayout();
            ((IWorkbenchPage)_pages[(int)page].View).FocusHeading();
        }
    }

    /// <summary>Navigates directly to the dedicated Settings page (Ctrl+,).</summary>
    public void OpenSettings() => Navigate(WorkbenchPage.Settings, NavigationOrigin.Shortcut);

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (HideOnClose && !e.Cancel)
        {
            e.Cancel = true;
            Hide();
        }
    }

    internal event Action<QuestionItem>? NotificationWanted;

    internal void Attach(ApprovalQueueView view)
    {
        ApprovalsPage.View = view;
        view.Arrived += OnArrived;
    }

    internal void AttachProject(ProjectView view) => ProjectPage.Attach(view);

    internal void AttachPolicy(SafetyPolicyStore policy, string? auditFolder = null)
    {
        _policy = policy;
        _policy.Changed += () => Dispatcher.BeginInvoke(ApplyStatus);
        ApprovalsPage.AttachPolicy(policy, auditFolder);
        ApplyStatus();
    }

    internal void AttachAudit(AuditView view, IShellOpen? shell = null)
    {
        AuditPage.Attach(view, shell);
        AuditPage.RecordOpened += OpenRecord;
    }

    internal void AttachLogs(LogsView view, IShellOpen? shell = null) => LogsPage.Attach(view, shell);

    internal void AttachSettings(StartAtLogin login, string configFile, SafetyPolicyStore? policy = null, IShellOpen? shell = null) =>
        SettingsPage.Attach(login, configFile, policy ?? _policy, shell);

    internal void AttachStatus(StatusView view) => StatusPage.Attach(view);

    internal void AttachClients(ClientsView view) => ClientsPage.Attach(view);

    internal void AttachMcp(Vm.VmView vm, ClientsView? clients = null, string? hostExecutable = null) =>
        McpPage.Attach(vm, clients, hostExecutable);

    internal void OpenRecord(AuditRecord record)
    {
        _recordOpener = Keyboard.FocusedElement;
        RecordLayer.Visibility = Visibility.Visible;
        UpdateLayout();
        RecordPane.Show(record);
    }

    internal void CloseRecord()
    {
        RecordLayer.Visibility = Visibility.Collapsed;
        Keyboard.Focus(_recordOpener ?? AuditPage.Heading);
        _recordOpener = null;
    }

    internal static Reminder ReminderFor(bool visible, bool minimized, bool active) =>
        (active ? Reminder.None : Reminder.Flash) | (!visible || minimized ? Reminder.Notify : Reminder.None);

    internal void Open(QuestionItem? question)
    {
        if (RecordLayer.Visibility == Visibility.Visible)
        {
            CloseRecord();
        }

        if (CommandLayer.Visibility == Visibility.Visible)
        {
            CloseCommandPalette();
        }

        Restore();
        if (question is not null)
        {
            ApprovalsPage.Open(question);
        }

        if (question is not null || _pending > 0)
        {
            Navigate(WorkbenchPage.Approvals, NavigationOrigin.Reminder);
        }
    }

    internal void ShowQueue(ApprovalQueue queue, bool listening) => ShowApprovals(
        !listening ? ApprovalChannelState.NameTaken : queue.IsAccepting ? ApprovalChannelState.Accepting : ApprovalChannelState.Full,
        queue.PendingCount);

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

    public void Restore()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void CanNavigate(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = RecordLayer.Visibility != Visibility.Visible && CommandLayer.Visibility != Visibility.Visible;

    private void ToggleLeftPane()
    {
        _leftPaneOpen = !_leftPaneOpen;
        ProjectPage.SetLeftPaneVisible(_leftPaneOpen);
        SettingsPage.SetLeftPaneVisible(_leftPaneOpen);
    }

    private void OpenCommandPalette()
    {
        _commandOpener = Keyboard.FocusedElement;
        Loc loc = Loc.Current;
        var items = new List<PaletteEntry>();
        foreach ((WorkbenchPage page, string key, string shortcut, _, _) in _pages)
        {
            items.Add(new PaletteEntry(loc.T("Shell.Shortcut", loc[key], shortcut), () => Navigate(page, NavigationOrigin.Shortcut)));
        }

        items.Add(new PaletteEntry(loc["Shell.CmdToggleApproval"], ToggleApprovalPolicy));
        items.Add(new PaletteEntry(loc["Shell.CmdToggleTheme"], ToggleTheme));
        items.Add(new PaletteEntry(loc["Shell.CmdToggleAccent"], () => AccentManager.Current.Accent = AccentManager.Current.Accent == BrandAccent.Cobalt ? BrandAccent.Petrol : BrandAccent.Cobalt));
        items.Add(new PaletteEntry(loc["Shell.CmdToggleLang"], ToggleLanguage));

        _paletteAll = [.. items];
        CommandSearch.Text = string.Empty;
        CommandList.ItemsSource = _paletteAll;
        CommandList.SelectedIndex = 0;
        CommandLayer.Visibility = Visibility.Visible;
        UpdateLayout();
        Keyboard.Focus(CommandSearch);
    }

    private void CloseCommandPalette()
    {
        CommandLayer.Visibility = Visibility.Collapsed;
        Keyboard.Focus(_commandOpener ?? _pages[(int)CurrentPage].Item);
        _commandOpener = null;
    }

    private void ExecutePaletteSelection()
    {
        if (CommandList.SelectedItem is PaletteEntry entry)
        {
            CloseCommandPalette();
            entry.Execute();
        }
    }

    private void OnCommandSearchChanged(object sender, TextChangedEventArgs e)
    {
        string q = CommandSearch.Text.Trim();
        CommandList.ItemsSource = q.Length == 0
            ? _paletteAll
            : _paletteAll.Where(p => p.Label.Contains(q, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (CommandList.Items.Count > 0)
        {
            CommandList.SelectedIndex = 0;
        }
    }

    private void OnCommandPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseCommandPalette();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ExecutePaletteSelection();
        }
        else if (e.Key == Key.Down && CommandList.Items.Count > 0)
        {
            CommandList.SelectedIndex = Math.Min(CommandList.Items.Count - 1, CommandList.SelectedIndex + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && CommandList.Items.Count > 0)
        {
            CommandList.SelectedIndex = Math.Max(0, CommandList.SelectedIndex - 1);
            e.Handled = true;
        }
    }

    private void OnCommandListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ExecutePaletteSelection();
        }
    }

    private void OnCommandPick(object sender, MouseButtonEventArgs e) => ExecutePaletteSelection();

    private void OnCommandScrim(object sender, MouseButtonEventArgs e) => CloseCommandPalette();

    private void OnArrived(QuestionItem item)
    {
        Reminder reminder = ReminderFor(IsVisible, WindowState == WindowState.Minimized, IsActive);
        if (reminder.HasFlag(Reminder.Flash) && new WindowInteropHelper(this).Handle is var handle && handle != IntPtr.Zero)
        {
            var flash = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = handle, Flags = FlashTray | FlashUntilForeground };
            FlashWindowEx(ref flash);
        }

        if (reminder.HasFlag(Reminder.Notify))
        {
            NotificationWanted?.Invoke(item);
        }
    }

    private void MoveRegion(int step)
    {
        var regions = new List<(UIElement Region, Action Focus)>
        {
            (Sider, () => Keyboard.Focus(_pages[(int)CurrentPage].Item)),
            (Header, () => Keyboard.Focus(PendingBadge.IsVisible ? PendingBadge : MinimizeButton)),
        };
        if (CurrentPage == WorkbenchPage.Approvals)
        {
            regions.Add((ApprovalsPage, ApprovalsPage.FocusList));
            if (ApprovalsPage.DetailArea.IsVisible)
            {
                regions.Add((ApprovalsPage.DetailArea, ApprovalsPage.FocusHeading));
            }
        }
        else
        {
            regions.Add((Body, ((IWorkbenchPage)_pages[(int)CurrentPage].View).FocusHeading));
        }

        int current = regions.FindLastIndex(r => r.Region.IsKeyboardFocusWithin);
        int next = current < 0 ? (step > 0 ? 0 : regions.Count - 1) : (current + step + regions.Count) % regions.Count;
        regions[next].Focus();
    }

    private void Refresh()
    {
        switch (CurrentPage)
        {
            case WorkbenchPage.Project:
                ProjectPage.Refresh();
                break;
            case WorkbenchPage.Audit:
                AuditPage.Refresh();
                break;
            case WorkbenchPage.Status:
                StatusPage.Refresh();
                break;
            case WorkbenchPage.Clients:
                ClientsPage.Refresh();
                break;
            case WorkbenchPage.Logs:
                LogsPage.Refresh();
                break;
            case WorkbenchPage.Settings:
                SettingsPage.Refresh();
                break;
        }
    }

    private void Find()
    {
        Navigate(WorkbenchPage.Audit, NavigationOrigin.Shortcut);
        AuditPage.FocusFilters();
    }

    private void Cycle(int step) => Navigate((WorkbenchPage)(((int)CurrentPage + step + _pages.Length) % _pages.Length), NavigationOrigin.Shortcut);

    private void OnPendingBadge(object sender, RoutedEventArgs e) => Navigate(WorkbenchPage.Approvals, NavigationOrigin.Reminder);

    private void OnTogglePane(object sender, RoutedEventArgs e) => ToggleLeftPane();

    private void OnOpenCommandPalette(object sender, RoutedEventArgs e) => OpenCommandPalette();

    private void OnQuickTheme(object sender, RoutedEventArgs e) => ToggleTheme();

    private void OnQuickLang(object sender, RoutedEventArgs e) => ToggleLanguage();

    private void OnStatusApprovalToggle(object sender, RoutedEventArgs e) => ToggleApprovalPolicy();

    private void OnStatusAuditClick(object sender, RoutedEventArgs e) => Navigate(WorkbenchPage.Audit, NavigationOrigin.Shortcut);

    private static void ToggleTheme() =>
        ThemeManager.Current.Theme = ThemeManager.Current.EffectivelyDark ? AppTheme.Light : AppTheme.Dark;

    private static void ToggleLanguage() =>
        Loc.Current.Language = Loc.Current.Language == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese;

    private void ToggleApprovalPolicy()
    {
        if (_policy is { } policy)
        {
            ApprovalPolicyUi.Set(policy, !policy.RequireNormalWriteApproval, this);
        }
    }

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

        bool manual = _policy?.RequireNormalWriteApproval ?? true;
        string approvalLabel = loc[_policy?.WritesDenied == true ? "Shell.StatusWritesDenied" : manual ? "Shell.StatusApprovalOn" : "Shell.StatusApprovalOff"];
        ApprovalStatusText.Text = approvalLabel;
        AutomationProperties.SetName(ApprovalStatusButton, approvalLabel);
        // The design: a green dot and plain text while approval is on, an amber dot and amber semibold text while it is off.
        ApprovalStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, manual ? "State.Success.Text" : "State.Warning.Text");
        ApprovalStatusButton.SetResourceReference(ForegroundProperty, manual ? "Text.Secondary" : "State.Warning.Text");
        ApprovalStatusText.FontWeight = manual ? FontWeights.Normal : FontWeights.SemiBold;

        string pending = loc.T("Tray.Pending", _pending);
        PendingBadge.Visibility = _pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingBadge.Content = _pending.ToString(CultureInfo.CurrentCulture);
        PendingBadge.ToolTip = pending;
        AutomationProperties.SetName(PendingBadge, pending);
        ApprovalsItem.Tag = _pending > 0 ? _pending.ToString(CultureInfo.CurrentCulture) : null;
        AutomationProperties.SetHelpText(ApprovalsItem, _pending > 0 ? pending : string.Empty);
    }

    private void OnRecordClosed(object? sender, EventArgs e) => CloseRecord();

    private void OnRecordScrim(object sender, MouseButtonEventArgs e) => CloseRecord();

    private void OnRecordFolder(object? sender, EventArgs e) => RecordPane.ShowFolder(AuditPage.OpenFolder(), AuditPage.Folder);

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyTexts();

    private void OnMenuClick(object sender, RoutedEventArgs e) =>
        Navigate(_pages.Single(p => ReferenceEquals(p.Item, sender)).Page, NavigationOrigin.Menu);

    private void Rearm(bool shown)
    {
        if (shown && IsVisible && WindowState != WindowState.Minimized && CurrentPage == WorkbenchPage.Approvals)
        {
            ApprovalsPage.Rearm();
        }
    }

    private void Refocus()
    {
        if (ApprovalsPage.HasAnswerFocus)
        {
            ApprovalsPage.FocusHeading();
        }
    }

    private void ApplyWindowState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        Rearm(_lastState == WindowState.Minimized && WindowState != WindowState.Minimized);
        _lastState = WindowState;

        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeGlyph.Data = Geometry.Parse(maximized ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z" : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        Loc loc = Loc.Current;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(loc.Language == AppLanguage.Chinese ? "zh-CN" : "en-US");
        string page = loc[_pages[(int)CurrentPage].Key];
        Title = loc.T("Shell.WindowTitle", page);
        CurrentPageText.Text = "/ " + page;
        LangToggleButton.Content = loc[loc.Language == AppLanguage.Chinese ? "Settings.Chinese" : "Settings.English"];
        // A moon while the theme is light (the click goes dark), a sun while it is dark.
        ThemeGlyph.Data = Geometry.Parse(ThemeManager.Current.EffectivelyDark
            ? "M17,12 A5,5 0 1 1 7,12 A5,5 0 1 1 17,12 M12,1 V3 M12,21 V23 M4.22,4.22 L5.64,5.64 M18.36,18.36 L19.78,19.78 M1,12 H3 M21,12 H23 M4.22,19.78 L5.64,18.36 M18.36,5.64 L19.78,4.22"
            : "M21,12.79 A9,9 0 1 1 11.21,3 A7,7 0 0 0 21,12.79 Z");
        foreach ((_, string key, string shortcut, RadioButton item, _) in _pages)
        {
            item.ToolTip = loc.T("Shell.Shortcut", loc[key], shortcut);
        }

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

    private const uint FlashTray = 0x2;

    private const uint FlashUntilForeground = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
