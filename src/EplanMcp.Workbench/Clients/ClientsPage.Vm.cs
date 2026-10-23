using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Clients;

public partial class ClientsPage
{
    private void OnMode(object sender, RoutedEventArgs e)
    {
        if (_view is not { State: not ClientsState.Running } view)
        { return; }
        view.SetVmMode(VmChoice.IsChecked == true);
        VmPassword.Clear();
        StatusText.Text = string.Empty;
        Show();
    }

    private void OnVmInput(object sender, RoutedEventArgs e) => _view?.SetVmInput(VmAddress.Text, VmPort.Text, VmPassword.Password);
    private void OnTestVm(object sender, RoutedEventArgs e)
    { if (_view is { } view) { _ = view.TestConnectionAsync(CancellationToken.None); } }

    private void FocusVmError() => (_view!.VmAddressError.Length > 0 ? (Control)VmAddress : _view.VmPortError.Length > 0 ? VmPort : VmPassword).Focus();

    private void ShowVm(ClientsView view)
    {
        PageCaption.Text = Loc.Current[view.VmMode ? "VmHost.Caption" : "Clients.Caption"];
        VmHost.Visibility = view.VmMode ? Visibility.Visible : Visibility.Collapsed;
        LocalPlugin.Visibility = view.VmMode ? Visibility.Collapsed : Visibility.Visible;
        if (!view.VmMode)
        { VmPassword.Clear(); }
        foreach (Control control in new Control[] { LocalChoice, VmChoice, VmAddress, VmPort, VmPassword })
        {
            control.IsEnabled = view.State != ClientsState.Running;
            AutomationProperties.SetHelpText(control, control.IsEnabled ? string.Empty : Loc.Current["Clients.Running"]);
            control.ToolTip = control.IsEnabled ? null : Loc.Current["Clients.Running"];
            ToolTipService.SetShowOnDisabled(control, true);
        }
        foreach ((Control field, TextBlock error, string text, string label) in new[]
        {
            ((Control)VmAddress, VmAddressError, view.VmAddressError, "VmHost.Address"),
            (VmPort, VmPortError, view.VmPortError, "VmHost.Port"), (VmPassword, VmKeyError, view.VmKeyError, "VmHost.Key"),
        })
        {
            AutomationProperties.SetName(field, WorkbenchDialog.Plain(Loc.Current[label]));
            string help = field.IsEnabled ? text : Loc.Current["Clients.Running"];
            AutomationProperties.SetHelpText(field, help);
            field.ToolTip = help;
            field.SetResourceReference(BorderBrushProperty, text.Length > 0 ? "Primer.diffRed" : "Primer.inputBorder");
            if (error.Text != text)
            { error.Text = text; AccessibleStatus.Announce(error); }
        }
        string reason = view.VmReason();
        TestVm.IsEnabled = reason.Length == 0;
        AutomationProperties.SetHelpText(TestVm, reason);
        TestVm.ToolTip = reason;
        ToolTipService.SetShowOnDisabled(TestVm, true);
        if (VmTestStatus.Text != view.VmStatus)
        { VmTestStatus.Text = view.VmStatus; AccessibleStatus.Announce(VmTestStatus); }
    }
}
