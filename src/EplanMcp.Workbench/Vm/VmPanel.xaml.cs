using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Vm;

public partial class VmPanel : UserControl
{
    private VmView? _view;
    private bool _loaded;

    public VmPanel()
    {
        InitializeComponent();
        Confirm = _ => true;
        Loaded += (_, _) => { Loc.Current.LanguageChanged += OnLanguage; Show(); };
        Unloaded += (_, _) => Loc.Current.LanguageChanged -= OnLanguage;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && !_loaded && _view is { Reason.Length: 0 } view)
            {
                _loaded = true;
                _ = view.RunAsync("instances", Confirm, CopyText, CancellationToken.None);
            }
        };
    }

    internal Func<string, bool> Confirm { get; set; }

    internal Action<string> CopyText { get; set; } = Clipboard.SetText;

    internal void Attach(VmView view)
    {
        Address.ItemsSource = view.Addresses;
        Address.SelectedItem = view.Address;
        Port.Text = view.Port;
        HostAddress.Text = view.HostAddress;
        _view = view;
        view.PropertyChanged += (_, _) => Show();
        Show();
    }

    private void OnInstance(object sender, SelectionChangedEventArgs e)
    {
        if (_view is { } view)
        {
            view.SelectedProcess = Instance.SelectedItem as int?;
            Show();
        }
    }

    private void OnLanguage(object? sender, EventArgs e) => Show();

    private void OnInput(object sender, RoutedEventArgs e)
    {
        if (_view is { } view)
        {
            (view.Address, view.Port, view.HostAddress) = (Address.SelectedItem as string ?? string.Empty, Port.Text, HostAddress.Text);
            Show();
        }
    }

    private void OnAction(object sender, RoutedEventArgs e)
    {
        if (_view is { } view && sender is ButtonBase { Tag: string action })
        {
            _ = view.RunAsync(action, Confirm, CopyText, CancellationToken.None);
        }
    }

    private void Show()
    {
        if (_view is not { } view)
        {
            IsEnabled = false;
            return;
        }

        IsEnabled = true;
        foreach ((FrameworkElement field, string key) in new (FrameworkElement, string)[]
            { (Address, "Vm.Address"), (Instance, "Vm.Instance"), (Port, "Vm.Port"), (HostAddress, "Vm.Host"), (FirewallValue, "Vm.Network") })
        {
            AutomationProperties.SetName(field, WorkbenchDialog.Plain(Loc.Current[key]));
        }

        foreach (ButtonBase button in new ButtonBase[] { Allow, Remove, Refresh, Instances, Start, Stop })
        {
            string reason = view.DisabledReason((string)button.Tag);
            button.IsEnabled = reason.Length == 0;
            ToolTipService.SetShowOnDisabled(button, true);
            AutomationProperties.SetHelpText(button, reason);
            button.ToolTip = reason.Length > 0 ? reason : button == Allow || button == Remove ? Loc.Current["Vm.Uac"] : null;
        }

        FirewallValue.Text = view.Firewall;
        // The design's code line: where the clients reach the service; no key, the firewall rule decides (ADR-0062).
        Endpoint.Text = Loc.Current.T("Vm.Endpoint", view.Address, view.Port);
        foreach ((Control field, TextBlock error, string text) in new[] { ((Control)Address, AddressError, view.AddressError), (Port, PortError, view.PortError), (HostAddress, HostError, view.HostError) })
        {
            if (error.Text != text)
            {
                error.Text = text;
                AccessibleStatus.Announce(error);
            }

            string help = field != HostAddress && view.Running ? Loc.Current["Vm.StopFirst"] : text;
            AutomationProperties.SetHelpText(field, help);
            field.ToolTip = help;
            field.SetResourceReference(BorderBrushProperty, text.Length > 0 ? "State.Warning.Text" : "Border.Control");
        }

        if (!ReferenceEquals(Instance.ItemsSource, view.Processes))
        {
            Instance.ItemsSource = view.Processes;
        }

        Instance.IsEnabled = Address.IsEnabled = Port.IsEnabled = !view.Busy && !view.Running;
        AutomationProperties.SetHelpText(Instance, view.Running ? Loc.Current["Vm.StopFirst"] : view.DisabledReason("start"));
        Instance.ToolTip = AutomationProperties.GetHelpText(Instance);
        HostAddress.IsEnabled = !view.Busy;
        // One place for start or stop, as the design's single service button.
        Start.Visibility = view.Running ? Visibility.Collapsed : Visibility.Visible;
        Stop.Visibility = view.Running ? Visibility.Visible : Visibility.Collapsed;
        Working.Visibility = view.Busy || view.Starting ? Visibility.Visible : Visibility.Collapsed;
        ServiceIcon.SetResourceReference(StyleProperty, view.ServiceFailed ? "Primer.AlertIcon.Error" : "Primer.AlertIcon.Info");
        if (ServiceStatus.Text != view.ServiceStatus)
        {
            ServiceStatus.Text = view.ServiceStatus;
            AccessibleStatus.Announce(ServiceStatus);
        }

        StatusIcon.SetResourceReference(StyleProperty, view.Failed ? "Primer.AlertIcon.Error" : "Primer.AlertIcon.Info");
        string status = view.Reason.Length > 0 ? view.Reason : view.Message;
        if (Status.Text != status)
        {
            Status.Text = status;
            AccessibleStatus.Announce(Status);
        }
    }
}
