using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
        ErrorBanner.Visibility = Visibility.Collapsed; // every message is a toast (owner, 2026-11-20)
        string errorText = view.Error is { } error ? loc.T("Status.Error", error) : string.Empty;
        if (errorText.Length > 0 && errorText != ErrorText.Text)
        {
            Toasts.Show(errorText, ToastTone.Error);
        }

        ErrorText.Text = errorText;
        ErrorDetails.Text = view.ErrorDetails ?? string.Empty;
        IReadOnlyList<StatusLine> suspensions = view.Suspensions;
        SuspensionBanner.Visibility = suspensions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Suspensions.ItemsSource = suspensions;

        // Workbench 4.0: what needs handling first, then what passed; each check with its group as a tag.
        CheckRow[] rows = [.. view.Groups.SelectMany(g => g.Lines.Select(l => new CheckRow(g.Key, l)))];
        CheckRow[] failing = [.. rows.Where(r => r.NeedsAction)];
        CheckRow[] passed = [.. rows.Where(r => !r.NeedsAction)];
        FailRows.ItemsSource = failing;
        PassRows.ItemsSource = ShowFailed.IsChecked == true ? [] : passed;
        FailSection.Visibility = failing.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PassSection.Visibility = passed.Length > 0 && ShowFailed.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        FailCaption.Text = loc.T("Status.FailCaption", failing.Length);
        PassCaption.Text = loc.T("Status.PassCaption", passed.Length);
        SummaryTag.Visibility = rows.Length > 0 && !running ? Visibility.Visible : Visibility.Collapsed;
        SummaryGlyph.Text = failing.Length > 0 ? "✕" : "✓";
        SummaryGlyph.Tag = failing.Length > 0 ? "Error" : "Success";
        SummaryText.Text = failing.Length > 0 ? loc.T("Status.NeedAction", failing.Length) : loc.T("Status.AllPassed", rows.Length);
    }

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (PassRows is not null)
        {
            Show();
        }
    }

    /// <summary>The add-in's fix: register it for the variants chosen, all of them when none is.</summary>
    private void OnFix(object sender, RoutedEventArgs e) => _ = FixAsync();

    internal Task FixAsync()
    {
        if (_view is not { } view)
        {
            return Task.CompletedTask;
        }

        if (!view.Variants.Any(v => v.IsSelected))
        {
            foreach (VariantChoice variant in view.Variants)
            {
                variant.IsSelected = true;
            }
        }

        return RegisterAsync(false);
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnVariantChanged(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(Show);

    private void OnRegister(object sender, RoutedEventArgs e) => _ = RegisterAsync(false);

    private void OnUnregister(object sender, RoutedEventArgs e) => _ = RegisterAsync(true);

    /// <summary>Registers or unregisters, then says in a toast what it came to; the per-variant lines stay below.</summary>
    internal async Task RegisterAsync(bool uninstall)
    {
        if (_view is not { } view)
        {
            return;
        }

        await view.RegisterAsync(uninstall, CancellationToken.None).ConfigureAwait(true);
        if (view.State == StatusState.Ready && view.Outcome is { } outcome)
        {
            Loc loc = Loc.Current;
            string text = Approvals.ProjectText.Line(loc.Language == AppLanguage.Chinese ? outcome.Zh : outcome.En, 300);
            Toasts.Show(outcome.Failed ? loc.T("Addin.Toast.Failed", text) : text, outcome.Failed ? ToastTone.Error : ToastTone.Success);
        }
    }
}
