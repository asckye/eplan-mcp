using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Status;

/// <summary>The status and environment checks page over <see cref="StatusView"/>: runs doctor on open and F5.</summary>
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

    public event Action? ExportDiagnosticsRequested;

    public FrameworkElement? Heading => PageTitle;

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    internal void Attach(StatusView view)
    {
        _view = view;
        view.PropertyChanged += OnViewChanged;
        Show();
    }

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
            AccessibleStatus.Announce(Caption);
        }

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

        StatusLine[] allLines = [.. view.Groups.SelectMany(g => g.Lines)];
        int okCount = allLines.Count(l => l.Status is CheckStatus.Ok or CheckStatus.Info);
        int warnCount = allLines.Count(l => l.Status == CheckStatus.Warning);
        int failCount = allLines.Count(l => l.Status == CheckStatus.Failure);
        StatPass.Text = okCount.ToString(CultureInfo.InvariantCulture);
        StatWarn.Text = warnCount.ToString(CultureInfo.InvariantCulture);
        StatFail.Text = failCount.ToString(CultureInfo.InvariantCulture);
        StatOverall.Text = failCount > 0 ? loc["Status.Failure"] : warnCount > 0 ? loc["Status.Warning"] : loc["Status.Ok"];
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnExportDiag(object sender, RoutedEventArgs e) => ExportDiagnosticsRequested?.Invoke();

    private void OnVariantChanged(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(Show);

    private void OnRegister(object sender, RoutedEventArgs e) => _ = _view?.RegisterAsync(false, CancellationToken.None);

    private void OnUnregister(object sender, RoutedEventArgs e) => _ = _view?.RegisterAsync(true, CancellationToken.None);
}
