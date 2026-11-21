using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Clients;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Vm;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Mcp;

/// <summary>One EPLAN year's tile: its name, whether it is installed and which variants, the newest one marked.</summary>
internal sealed record VersionTile(string Title, string State, bool Installed, bool Current, string Name);

public partial class McpPage : UserControl, IWorkbenchPage
{
    private ClientsView? _clients;
    private VmView? _vm;
    private string? _host;
    private bool _syncing;

    public McpPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Loc.Current.LanguageChanged -= OnLanguageChanged;
            Loc.Current.LanguageChanged += OnLanguageChanged;
            SyncMode();
        };
        Unloaded += (_, _) => Loc.Current.LanguageChanged -= OnLanguageChanged;
    }

    public event Action<WorkbenchPage>? NavigateRequested;

    public FrameworkElement? Heading => PageTitle;

    /// <summary>Where the EPLAN installations are looked for; a test points it elsewhere.</summary>
    internal string EplanRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EPLAN");

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    internal void Attach(VmView vm, ClientsView? clients, string? hostExecutable)
    {
        VmConnection.Attach(vm);
        _vm = vm;
        _clients = clients;
        _host = hostExecutable;
        vm.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(SyncMode);
        if (clients is not null)
        {
            clients.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(SyncMode);
        }

        SyncMode();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => SyncMode();

    private void SyncMode()
    {
        if (HttpSection is null)
        {
            return;
        }

        bool vm = _clients?.VmMode ?? (HttpChoice.IsChecked == true);
        _syncing = true;
        StdioChoice.IsChecked = !vm;
        HttpChoice.IsChecked = vm;
        HttpSection.Visibility = vm ? Visibility.Visible : Visibility.Collapsed;
        ApplyTags(vm);
        _syncing = false;
        ShowVersions();
    }

    private void OnTransportChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || HttpSection is null)
        {
            return;
        }

        bool vm = HttpChoice.IsChecked == true;
        HttpSection.Visibility = vm ? Visibility.Visible : Visibility.Collapsed;
        ApplyTags(vm);
        _clients?.SetVmMode(vm);
    }

    private void OnGoClients(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(WorkbenchPage.Clients);

    /// <summary>
    /// The bar's tag says the transport and, for HTTP, whether the service runs; the box's tag what it exposes on the
    /// network; the chosen tile carries the check (Workbench 4.0 design).
    /// </summary>
    private void ApplyTags(bool vm)
    {
        Loc loc = Loc.Current;
        bool running = _vm?.Running == true;
        TransportTagText.Text = !vm ? loc["Mcp.TagStdio"] : running ? loc.T("Mcp.TagHttpRunning", _vm!.Port) : loc["Mcp.TagHttpStopped"];
        (TransportGlyph.Text, TransportGlyph.Tag) = !vm || running ? ("✓", "Success") : ("•", string.Empty);
        ExposureTagText.Text = loc[vm ? "Mcp.ExposureHttp" : "Mcp.ExposureStdio"];
        StdioCheck.Visibility = vm ? Visibility.Hidden : Visibility.Visible;
        HttpCheck.Visibility = vm ? Visibility.Visible : Visibility.Hidden;
        bool exists = _host is { Length: > 0 } && File.Exists(_host);
        HostPathBox.Text = _host ?? loc["Host.Missing"];
        HostPathLabel.Text = loc[exists ? "Mcp.HostRuns" : "Mcp.HostMissing"];
        HostPathLabel.SetResourceReference(TextBlock.ForegroundProperty, exists ? "State.Success.Text" : "State.Error.Text");
    }

    /// <summary>2024 to 2027 as tiles: the years installed here with their variants, the newest of them marked.</summary>
    private void ShowVersions()
    {
        Loc loc = Loc.Current;
        IReadOnlyList<EplanInstall> installs = EplanInstalls.Scan(EplanRoot);
        int newest = installs.Where(i => i.Installed).Select(i => i.Year).DefaultIfEmpty(0).Max();
        Versions.ItemsSource = installs.Select(i =>
        {
            string title = "EPLAN " + i.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string state = i.Installed ? "✓ " + string.Join(" / ", i.Variants) : loc["Mcp.NotInstalled"];
            return new VersionTile(title, state, i.Installed, i.Year == newest, title + ", " + state);
        }).ToList();
    }
}
