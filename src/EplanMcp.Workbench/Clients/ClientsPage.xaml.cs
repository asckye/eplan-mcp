using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Clients;

/// <summary>
/// The client page over its <see cref="ClientsView"/>: looks for the clients the first time it shows and on Refresh (F5);
/// writes and removals go through a confirmation that shows the diff, the safe answer the default (DLG-04, DLG-12).
/// </summary>
public partial class ClientsPage : UserControl, IWorkbenchPage
{
    private ClientsView? _view;
    private Task? _looked;

    public ClientsPage()
    {
        InitializeComponent();
        (MarketplaceCommand.Text, InstallCommand.Text) = (ClientsView.PluginCommands[0], ClientsView.PluginCommands[1]);
        Confirm = (title, message, safe, action, details) => WorkbenchDialog.Confirm(Window.GetWindow(this)!, title, message, safe, action, details);
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && _looked is null)
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

    /// <summary>
    /// The engineer's confirmation: title, message, safe and action labels, and the diff; true only for the action.
    /// Tests answer it themselves.
    /// </summary>
    internal Func<string, string, string, string, string?, bool> Confirm { get; set; }

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    internal void Attach(ClientsView view)
    {
        _view = view;
        view.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Show);
        Show();
    }

    /// <summary>A dry run of every client again (F5), unless one runs or there is no Host.</summary>
    internal void Refresh()
    {
        if (_view is { State: not ClientsState.Running, CanRun: true })
        {
            _looked = _view.DetectAsync(CancellationToken.None);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        Loc loc = Loc.Current;
        RefreshButton.ToolTip = loc.T("Shell.Shortcut", loc["Clients.Refresh"], "F5");
        if (_view is not { } view)
        {
            return;
        }

        bool running = view.State == ClientsState.Running;
        Working.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (running || !view.CanRun)
        {
            Say(loc[view.CanRun ? "Clients.Running" : "Host.Missing"]);
        }

        // NAV-05, CMD-04: nothing to run without a Host, and the buttons say why.
        string help = view.CanRun ? string.Empty : loc["Host.Missing"];
        foreach (Button button in new[] { RefreshButton, RemoveButton, WriteButton })
        {
            button.IsEnabled = view.CanRun && !running;
            AutomationProperties.SetHelpText(button, help);
        }

        ErrorBanner.Visibility = view.Error is null ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = view.Error is { } error ? loc.T("Status.Error", error) : string.Empty;
        ErrorDetails.Text = view.ErrorDetails ?? string.Empty;
        ErrorDetails.Visibility = view.ErrorDetails is null ? Visibility.Collapsed : Visibility.Visible;
        Cards.ItemsSource = null;
        Cards.ItemsSource = view.Cards;
    }

    private void OnWrite(object sender, RoutedEventArgs e) => _ = WriteAsync();

    private void OnRemove(object sender, RoutedEventArgs e) => _ = RemoveAsync();

    /// <summary>Previews the chosen clients, shows the diff, and writes only when the engineer confirms (PRI-01, DLG-12).</summary>
    private async Task WriteAsync()
    {
        if (Chosen() is not { } view)
        {
            return;
        }

        if (view.ToWrite.Count == 0)
        {
            Say(Loc.Current["Clients.NoneToWrite"]); // only Claude Code with its plugin: removing its old entry is still possible
            return;
        }

        await view.PreviewAsync(CancellationToken.None).ConfigureAwait(true);
        Loc loc = Loc.Current;
        string message = loc.T("Clients.WriteMessage", view.ToWrite.Count)
            + (view.ToWrite.Any(c => c.Name == "claude-desktop") ? " " + loc["Clients.QuitDesktop"] : string.Empty);
        if (view.State == ClientsState.Ready && Confirm(loc["Clients.WriteTitle"], message, loc["Exit.Cancel"], loc["Clients.WriteConfirm"], view.Diff()))
        {
            await ApplyAsync(view, ClientConfigMode.Write).ConfigureAwait(true);
        }
    }

    private async Task RemoveAsync()
    {
        if (Chosen() is not { } view)
        {
            return;
        }

        Loc loc = Loc.Current;
        string clients = string.Join(Environment.NewLine, view.Selected.Select(c => c.Title));
        if (Confirm(loc["Clients.RemoveTitle"], loc.T("Clients.RemoveMessage", view.Selected.Count), loc["Exit.Cancel"], loc["Clients.RemoveConfirm"], clients))
        {
            await ApplyAsync(view, ClientConfigMode.Remove).ConfigureAwait(true);
        }
    }

    /// <summary>The view with something chosen; else nothing runs, and the page says so (STS-05).</summary>
    private ClientsView? Chosen()
    {
        if (_view is { Selected.Count: > 0 } view)
        {
            return view;
        }

        Say(Loc.Current["Clients.NoneChosen"]);
        return null;
    }

    private async Task ApplyAsync(ClientsView view, ClientConfigMode mode)
    {
        await view.ApplyAsync(mode, CancellationToken.None).ConfigureAwait(true);
        Say(Loc.Current[view.State == ClientsState.Ready ? "Clients.Done" : "Clients.NotDone"]);
    }

    /// <summary>Shows and announces a status message (STS-02).</summary>
    private void Say(string text)
    {
        StatusText.Text = text;
        AccessibleStatus.Announce(StatusText);
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();
}
