using System.ComponentModel;
using EplanMcp.Workbench.Commands;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Vm;

/// <summary>
/// Key-free VM HTTP &amp; host-only IPv4 Windows Firewall control state.
/// </summary>
internal sealed partial class VmView(IHostRunner? host, IVmElevation elevation, IReadOnlyList<string> addresses, IVmLauncher? launcher = null) : INotifyPropertyChanged, IDisposable
{
    private string _message = "Vm.Help";
    private FirewallReport? _firewall;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> Addresses { get; } = addresses;

    public string Address { get; set; } = addresses.Count > 0 ? addresses[0] : string.Empty;

    public string Port { get; set; } = "8766";

    public string HostAddress { get; set; } = string.Empty;

    public bool Busy { get; private set; }

    public bool Failed => _message is "Vm.Failed" or "Vm.CancelledUnknown";

    public string AddressError => Addresses.Contains(Address) ? string.Empty : Loc.Current["Vm.AddressError"];

    public string PortError => VmNetwork.IsPort(Port) ? string.Empty : Loc.Current["Vm.PortError"];

    public string HostError => VmNetwork.IsAddress(HostAddress) ? string.Empty : Loc.Current["Vm.HostError"];

    public string Reason => host is null ? Loc.Current["Host.Missing"] : Busy ? Loc.Current["Vm.Working"] : string.Empty;

    public string Message => Loc.Current[_message];

    public string Firewall => _firewall is null ? Loc.Current["Vm.Unknown"] : _firewall.Rules.Count == 0 ? Loc.Current["Vm.Absent"]
        : string.Join(Environment.NewLine, _firewall.Rules.Select(r => Loc.Current.T("Vm.Rule", Approvals.ProjectText.Line(r.RemoteAddress), Approvals.ProjectText.Line(r.Port), Approvals.ProjectText.Line(r.Program), r.Profiles, r.Protocol, r.Direction, r.Action, r.Enabled, r.EdgeTraversal)));

    public string DisabledReason(string action) => host is null ? Loc.Current["Host.Missing"] : action == "stop" ? Running && !_stopping ? string.Empty : Loc.Current["Vm.Stopped"]
        : Running && action is "start" or "settings" or "instances" ? Loc.Current["Vm.StopFirst"]
        : Reason.Length > 0 ? Reason : action == "start" ? AddressError.Length > 0 ? AddressError : PortError.Length > 0 ? PortError
        : !_instancesKnown ? Loc.Current["Vm.CheckInstances"] : Processes.Count == 0 ? Loc.Current["Vm.NoEplan"]
        : Processes.Count > 1 && !Processes.Contains(SelectedProcess ?? 0) ? Loc.Current["Vm.PickInstance"] : string.Empty
        : action is "allow" or "remove" or "refresh" ? PortError.Length > 0 ? PortError : HostError : string.Empty;

    public async Task RunAsync(string action, Func<string, bool> confirm, Action<string> clipboard, CancellationToken cancellationToken)
    {
        _ = confirm;
        _ = clipboard;
        if (DisabledReason(action).Length > 0)
        {
            return;
        }

        if (action == "start")
        {
            await ServeAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (action == "stop")
        {
            StopService();
            return;
        }

        Busy = true;
        _message = "Vm.Help";
        Changed();
        try
        {
            switch (action)
            {
                case "instances":
                    _instancesKnown = false;
                    HostRun instances = await host!.RunAsync(["vm-instances", "--format", "json"], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(true);
                    Require(instances);
                    Processes = HostCommands.Parse<InstanceReport>(instances.Output).Processes;
                    _instancesKnown = Processes.All(p => p > 0) && Processes.Distinct().Count() == Processes.Count;
                    SelectedProcess = null;
                    _message = Processes.Count > 0 ? "Vm.Done" : "Vm.NoEplan";
                    break;
                case "allow":
                case "remove":
                    string port = Port, address = HostAddress; // Refresh exactly the scope submitted to Windows.
                    int? exit = await elevation.RunAsync(host!.Executable, action == "remove", port, address, cancellationToken).ConfigureAwait(true);
                    _message = exit is null ? "Vm.Cancelled" : "Vm.Working";
                    await ReadFirewallAsync(port, address, cancellationToken).ConfigureAwait(true);
                    _message = exit is null ? "Vm.Cancelled" : exit == 0 ? "Vm.Done" : "Vm.Failed";
                    break;
                case "refresh":
                    await ReadFirewallAsync(Port, HostAddress, cancellationToken).ConfigureAwait(true);
                    _message = "Vm.Done";
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _message = _message == "Vm.Cancelled" ? "Vm.CancelledUnknown" : "Vm.Failed";
        }
        finally
        {
            Busy = false;
            Changed();
        }
    }

    private async Task ReadFirewallAsync(string port, string address, CancellationToken cancellationToken)
    {
        _firewall = null;
        HostRun run = await host!.RunAsync(["vm-firewall", "show", "--port", port, "--host-address", address, "--format", "json"], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(true);
        Require(run);
        _firewall = HostCommands.Parse<FirewallReport>(run.Output);
    }

    private static void Require(HostRun run)
    {
        if (run.ExitCode != 0)
        {
            throw new InvalidOperationException("VM command failed.");
        }
    }

    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    internal sealed record FirewallReport(int SchemaVersion, IReadOnlyList<FirewallRule> Rules);

    internal sealed record FirewallRule(string Name, int Protocol, string Port, string Program, string RemoteAddress, int Profiles, int Direction, int Action, bool Enabled, bool EdgeTraversal);
}
