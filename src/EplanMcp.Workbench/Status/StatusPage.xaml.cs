using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Status;

/// <summary>The status page over its <see cref="StatusView"/>: runs doctor each time the page opens and on Refresh (F5).</summary>
public partial class StatusPage : UserControl, IWorkbenchPage
{
    private StatusView? _view;

    public StatusPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
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

    internal void Attach(StatusView view)
    {
        _view = view;
        view.PropertyChanged += OnViewChanged;
        Show();
    }

    /// <summary>Runs doctor, unless it is running or there is no Host to run.</summary>
    internal void Refresh()
    {
        if (_view is { State: not StatusState.Running, CanRun: true })
        {
            _ = _view.RunAsync(CancellationToken.None);
        }
    }

    private void OnViewChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Show);

    private void OnLanguageChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        Loc loc = Loc.Current;
        RefreshButton.ToolTip = loc.T("Shell.Shortcut", loc["Status.Refresh"], "F5");
        if (_view is not { } view)
        {
            return;
        }

        bool running = view.State == StatusState.Running;
        string caption = !view.CanRun ? loc["Host.Missing"] : running ? loc[view.Activity] : view.Summary ?? string.Empty;
        if (Caption.Text != caption)
        {
            Caption.Text = caption;
            AccessibleStatus.Announce(Caption); // STS-02: doctor finished, or failed
        }

        // NAV-05: without a Host the page says why and Refresh stays off.
        RefreshButton.IsEnabled = view.CanRun && !running;
        AutomationProperties.SetHelpText(RefreshButton, view.CanRun ? string.Empty : loc["Host.Missing"]);
        Variants.ItemsSource = view.Variants;
        Variants.IsEnabled = view.CanRun && !running;
        VariantHint.Text = !view.CanRun ? loc["Host.Missing"] : running ? loc[view.Activity] : loc[view.Variants.Count == 0 ? "Addin.NoVariants" : "Addin.Choose"];
        foreach (Button button in new[] { RegisterButton, UnregisterButton })
        {
            bool chosen = button == UnregisterButton || view.Variants.Any(v => v.IsSelected);
            button.IsEnabled = view.CanRun && !running && chosen;
            string help = !view.CanRun ? loc["Host.Missing"] : running ? loc[view.Activity] : !chosen ? loc["Addin.Choose"] : loc["Addin.Help"];
            AutomationProperties.SetHelpText(button, help);
            button.ToolTip = help;
        }
        Working.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ErrorBanner.Visibility = view.Error is null ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = view.Error is { } error ? loc.T("Status.Error", error) : string.Empty;
        ErrorDetails.Text = view.ErrorDetails ?? string.Empty;
        IReadOnlyList<StatusLine> suspensions = view.Suspensions;
        SuspensionBanner.Visibility = suspensions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Suspensions.ItemsSource = suspensions;
        Groups.ItemsSource = view.Groups;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnVariantChanged(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(Show);

    private void OnRegister(object sender, RoutedEventArgs e) => _ = _view?.RegisterAsync(false, CancellationToken.None);

    private void OnUnregister(object sender, RoutedEventArgs e) => _ = _view?.RegisterAsync(true, CancellationToken.None);
}
