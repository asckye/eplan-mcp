using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Clients;

public partial class ClientsPage
{
    public event Action<WorkbenchPage>? NavigateRequested;

    private void OnMode(object sender, RoutedEventArgs e)
    {
        if (_view is not { State: not ClientsState.Running } view)
        {
            return;
        }

        view.SetVmMode(VmChoice.IsChecked == true);
        StatusText.Text = string.Empty;
        Show();
    }

    private void OnVmInput(object sender, RoutedEventArgs e) => _view?.SetVmInput(VmAddress.Text, VmPort.Text);

    private void OnTestVm(object sender, RoutedEventArgs e)
    {
        if (_view is { } view)
        {
            _ = view.TestConnectionAsync(CancellationToken.None);
        }
    }

    private void OnEditMcp(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke(WorkbenchPage.Mcp);

    private void OnSelectDetected(object sender, RoutedEventArgs e)
    {
        if (_view is not { } view)
        {
            return;
        }

        foreach (ClientCard card in view.Cards)
        {
            card.IsSelected = card.Detected;
        }

        Show();
    }

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        if (_view is not { } view)
        {
            return;
        }

        foreach (ClientCard card in view.Cards)
        {
            card.IsSelected = false;
        }

        Show();
    }

    private void FocusVmError() => (_view!.VmAddressError.Length > 0 ? VmAddress : VmPort).Focus();

    private void ShowVm(ClientsView view)
    {
        LocalChoice.IsChecked = !view.VmMode;
        VmChoice.IsChecked = view.VmMode;
        TransportBadge.Text = Loc.Current[view.VmMode ? "Clients.TransportHttp" : "Clients.TransportStdio"];
        VmHost.Visibility = view.VmMode ? Visibility.Visible : Visibility.Collapsed;
        LocalPlugin.Visibility = view.VmMode ? Visibility.Collapsed : Visibility.Visible;
        foreach (Control control in new Control[] { LocalChoice, VmChoice, VmAddress, VmPort })
        {
            control.IsEnabled = view.State != ClientsState.Running;
            AutomationProperties.SetHelpText(control, control.IsEnabled ? string.Empty : Loc.Current["Clients.Running"]);
            control.ToolTip = control.IsEnabled ? null : Loc.Current["Clients.Running"];
            ToolTipService.SetShowOnDisabled(control, true);
        }

        foreach ((Control field, TextBlock error, string text, string label) in new[]
        {
            ((Control)VmAddress, VmAddressError, view.VmAddressError, "VmHost.Address"),
            (VmPort, VmPortError, view.VmPortError, "VmHost.Port"),
        })
        {
            AutomationProperties.SetName(field, WorkbenchDialog.Plain(Loc.Current[label]));
            string help = field.IsEnabled ? text : Loc.Current["Clients.Running"];
            AutomationProperties.SetHelpText(field, help);
            field.ToolTip = help;
            field.SetResourceReference(BorderBrushProperty, text.Length > 0 ? "State.Error.Text" : "Border.Control");
            if (error.Text != text)
            {
                error.Text = text;
                AccessibleStatus.Announce(error);
            }
        }

        string reason = view.VmReason();
        TestVm.IsEnabled = reason.Length == 0;
        AutomationProperties.SetHelpText(TestVm, reason);
        TestVm.ToolTip = reason;
        ToolTipService.SetShowOnDisabled(TestVm, true);
        if (VmTestStatus.Text != view.VmStatus)
        {
            VmTestStatus.Text = view.VmStatus;
            if (view.VmStatus != Loc.Current["VmHost.Help"])
            {
                Toasts.Show(view.VmStatus, ToastTone.Info);
            }
        }
    }
}
