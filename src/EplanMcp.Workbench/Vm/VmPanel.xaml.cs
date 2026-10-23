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
        Confirm = kind => WorkbenchDialog.Confirm(Window.GetWindow(this)!, Loc.Current["Vm." + kind + "Title"], Loc.Current["Vm." + kind + "Message"], Loc.Current["Exit.Cancel"], Loc.Current["Vm." + kind + "Confirm"]);
        Loaded += (_, _) => { Loc.Current.LanguageChanged += OnLanguage; Show(); };
        Unloaded += (_, _) => Loc.Current.LanguageChanged -= OnLanguage;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && !_loaded && _view is { Reason.Length: 0 } view)
            {
                _loaded = true;
                _ = view.RunAsync("load", Confirm, CopyText, CancellationToken.None);
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
        AutomationProperties.SetName(Address, WorkbenchDialog.Plain(Loc.Current["Vm.Address"]));
        foreach (ButtonBase button in new ButtonBase[] { Generate, Reveal, Copy, Delete, Allow, Remove, Refresh })
        {
            string reason = view.DisabledReason((string)button.Tag);
            button.IsEnabled = reason.Length == 0;
            AutomationProperties.SetHelpText(button, reason);
            button.ToolTip = reason.Length > 0 ? reason : button == Allow || button == Remove ? Loc.Current["Vm.Uac"] : null;
        }

        (Generate.Content, KeyValue.Text, Reveal.IsChecked, FirewallValue.Text) = (view.KeyAction, view.DisplayKey, view.Shown, view.Firewall);
        foreach ((Control field, TextBlock error, string text) in new[] { ((Control)Address, AddressError, view.AddressError), (Port, PortError, view.PortError), (HostAddress, HostError, view.HostError) })
        {
            if (error.Text != text)
            {
                error.Text = text;
                AccessibleStatus.Announce(error);
            }
            AutomationProperties.SetHelpText(field, text);
            field.SetResourceReference(BorderBrushProperty, text.Length > 0 ? "Primer.warn" : "Primer.inputBorder");
        }

        Address.IsEnabled = Port.IsEnabled = HostAddress.IsEnabled = !view.Busy;
        Working.Visibility = view.Busy ? Visibility.Visible : Visibility.Collapsed;
        StatusIcon.SetResourceReference(StyleProperty, view.Failed ? "Primer.AlertIcon.Error" : "Primer.AlertIcon.Info");
        string status = view.Reason.Length > 0 ? view.Reason : view.Message;
        if (Status.Text != status)
        {
            Status.Text = status;
            AccessibleStatus.Announce(Status);
        }
    }
}
