using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Vm;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Clients;

internal sealed partial class ClientsView
{
    private string _vmKey = string.Empty; // Transient only; never serialized, bound to UIA or included in diagnostics.
    private VmConnectionReport? _connection;
    public bool VmMode { get; private set; }
    public string VmAddress { get; private set; } = string.Empty;
    public string VmPort { get; private set; } = "8766";
    private string VmUrl => $"http://{VmAddress}:{VmPort}/mcp";
    public string VmAddressError => VmNetwork.IsAddress(VmAddress) ? string.Empty : Loc.Current["Vm.HostError"];
    public string VmPortError => VmNetwork.IsPort(VmPort) ? string.Empty : Loc.Current["Vm.PortError"];
    public string VmKeyError => _vmKey.Length == 43 && _vmKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        && "AEIMQUYcgkosw048".Contains(_vmKey[^1], StringComparison.Ordinal) ? string.Empty : Loc.Current["VmHost.KeyError"];
    public string VmStatus => _connection is null ? Loc.Current["VmHost.Help"] : _connection.Failure is { } failure ? Loc.Current["VmHost." + Key(failure.Replace('_', '-'))]
        : Loc.Current.T("VmHost.Connected", ProjectText.Line(_connection.Server!.Name), ProjectText.Line(_connection.Server.Version), _connection.ToolCount);

    public string VmReason(bool remove = false) => !CanRun ? Loc.Current["Host.Missing"] : State == ClientsState.Running ? Loc.Current["Clients.Running"]
        : !VmMode || remove ? string.Empty : VmAddressError.Length > 0 ? VmAddressError : VmPortError.Length > 0 ? VmPortError : VmKeyError;

    public void SetVmMode(bool vm)
    {
        if (State == ClientsState.Running || vm == VmMode)
        { return; }
        (VmMode, _vmKey, _connection, _error, _refused, State) = (vm, string.Empty, null, null, null, ClientsState.Idle);
        Cards = [.. Names.Where(n => !vm || n is "claude-code" or "codex" or "gemini-cli" or "qwen-code" or "kimi-code"
            or "codebuddy" or "opencode" or "qwen-agent" or "cursor" or "vscode-copilot").Select(n => new ClientCard(n))];
        PluginCommands = [string.Empty, string.Empty];
        PluginPreview = null;
        Changed();
    }

    public void SetVmInput(string address, string port, string key)
    {
        if (!VmMode || State == ClientsState.Running)
        { return; }
        (VmAddress, VmPort, _vmKey, _connection, _error, _refused, State) = (address, port, key, null, null, null, ClientsState.Idle);
        Changed();
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!VmMode || VmReason().Length > 0)
        { return; }
        (State, _mode, _connection, _error, _refused) = (ClientsState.Running, ClientConfigMode.DryRun, null, null, null);
        Changed();
        try
        {
            VmConnectionReport report = await _host.TestVmAsync(VmUrl, _vmKey, cancellationToken).ConfigureAwait(true);
            bool valid = report.Failure is null ? report.Server is not null && !string.IsNullOrWhiteSpace(report.Server.Name) && !string.IsNullOrWhiteSpace(report.Server.Version) && report.ToolCount is >= 0
                : report.Server is null && report.ToolCount is null && report.Failure is "unreachable" or "timed_out" or "unauthorized" or "forbidden" or "rate_limited" or "protocol_error";
            if (!valid)
            { throw new HostCommandException(HostFailure.Unreadable, "Invalid VM connection result."); }
            (_connection, State) = (report, report.Failure is null ? ClientsState.Ready : ClientsState.Failed);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { (_error, State) = (ex is HostCommandException error ? error : new HostCommandException(HostFailure.Unreadable, "VM connection test failed."), ClientsState.Failed); }
        finally { Changed(); }
    }
}
