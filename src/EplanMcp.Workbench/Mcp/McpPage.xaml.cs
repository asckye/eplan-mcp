using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Clients;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Vm;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Mcp;

public partial class McpPage : UserControl, IWorkbenchPage
{
    private ClientsView? _clients;
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

    public void FocusHeading() => Keyboard.Focus(PageTitle);

    internal void Attach(VmView vm, ClientsView? clients, string? hostExecutable)
    {
        VmConnection.Attach(vm);
        _clients = clients;
        HostPathBox.Text = hostExecutable ?? Loc.Current["Host.Missing"];
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
        _syncing = false;
    }

    private void OnTransportChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || HttpSection is null)
        {
            return;
        }

        bool vm = HttpChoice.IsChecked == true;
        HttpSection.Visibility = vm ? Visibility.Visible : Visibility.Collapsed;
        _clients?.SetVmMode(vm);
    }

    private void OnGoClients(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(WorkbenchPage.Clients);
}
