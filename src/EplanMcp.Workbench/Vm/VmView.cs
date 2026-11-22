using System.ComponentModel;
using EplanMcp.Workbench.Commands;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Vm;

/// <summary>
/// The VM connection (ADR-0062, ADR-0067): no key, no host address and no pinned EPLAN; the clients need only the MCP
/// address, and a Windows Firewall rule for the local subnet lets them in.
/// </summary>
internal sealed partial class VmView(IHostRunner? host, IVmElevation elevation, IReadOnlyList<string> addresses, IVmLauncher? launcher = null) : INotifyPropertyChanged, IDisposable
{
    private string _message = "Vm.Help";
    private FirewallReport? _firewall;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> Addresses { get; } = addresses;

    public string Address { get; set; } = addresses.Count > 0 ? addresses[0] : string.Empty;

    public string Port { get; set; } = "8766";

    public bool Busy { get; private set; }

    public bool Failed => _message is "Vm.Failed" or "Vm.CancelledUnknown";

    public string AddressError => Addresses.Contains(Address) ? string.Empty : Loc.Current["Vm.AddressError"];

    public string PortError => VmNetwork.IsPort(Port) ? string.Empty : Loc.Current["Vm.PortError"];

    public string Reason => host is null ? Loc.Current["Host.Missing"] : Busy ? Loc.Current["Vm.Working"] : string.Empty;

    public string Message => Loc.Current[_message];

    public string Firewall => _firewall is null ? Loc.Current["Vm.Unknown"] : _firewall.Rules.Count == 0 ? Loc.Current["Vm.Absent"]
        : string.Join(Environment.NewLine, _firewall.Rules.Select(r => Loc.Current.T("Vm.Rule", Approvals.ProjectText.Line(r.RemoteAddress), Approvals.ProjectText.Line(r.Port), Approvals.ProjectText.Line(r.Program), r.Profiles, r.Protocol, r.Direction, r.Action, r.Enabled, r.EdgeTraversal)));

    /// <summary>The address the AI clients are given: the only thing they need to connect (ADR-0067).</summary>
    public string McpUrl => $"http://{Address}:{Port}/mcp";

    public string DisabledReason(string action) => host is null ? Loc.Current["Host.Missing"] : action == "stop" ? Running && !_stopping ? string.Empty : Loc.Current["Vm.Stopped"]
        : action == "copy" ? AddressError.Length > 0 ? AddressError : PortError
        : Running && action is "start" or "settings" ? Loc.Current["Vm.StopFirst"]
        : Reason.Length > 0 ? Reason : action == "start" ? AddressError.Length > 0 ? AddressError : PortError
        : action is "allow" or "remove" or "refresh" ? PortError : string.Empty;

    public async Task RunAsync(string action, Func<string, bool> confirm, Action<string> clipboard, CancellationToken cancellationToken)
    {
        _ = confirm;
        if (DisabledReason(action).Length > 0)
        {
            return;
        }

        if (action == "copy")
        {
            clipboard(McpUrl);
            _message = "Vm.Copied";
            Changed();
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
                case "allow":
                case "remove":
                    string port = Port; // Refresh exactly the port submitted to Windows.
                    int? exit = await elevation.RunAsync(host!.Executable, action == "remove", port, cancellationToken).ConfigureAwait(true);
                    _message = exit is null ? "Vm.Cancelled" : "Vm.Working";
                    await ReadFirewallAsync(port, cancellationToken).ConfigureAwait(true);
                    _message = exit is null ? "Vm.Cancelled" : exit == 0 ? "Vm.Done" : "Vm.Failed";
                    break;
                case "refresh":
                    await ReadFirewallAsync(Port, cancellationToken).ConfigureAwait(true);
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

    private async Task ReadFirewallAsync(string port, CancellationToken cancellationToken)
    {
        _firewall = null;
        HostRun run = await host!.RunAsync(["vm-firewall", "show", "--port", port, "--format", "json"], TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(true);
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
