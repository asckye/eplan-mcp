using System.Text.RegularExpressions;
using EplanMcp.Workbench.Approvals;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Vm;

// The UI thread owns both command state and child lifetime; the launcher never calls back into this view.
internal sealed partial class VmView
{
    private IVmChild? _child;
    private bool _instancesKnown, _stopping, _disposed;
    private string _serviceState = "Vm.Stopped", _serviceError = string.Empty;
    private VmEndpoint? _bound;
    public IReadOnlyList<int> Processes { get; private set; } = [];
    public int? SelectedProcess { get; set; }
    public bool Running => _child is not null;
    public bool Starting => _serviceState == "Vm.Starting";
    public string ServiceStatus => _serviceState == "Vm.Listening" && _bound is { } bound
        ? Loc.Current.T("Vm.Listening", bound.Address, bound.Port) : Loc.Current[_serviceState] + (_serviceError.Length > 0 ? Environment.NewLine + _serviceError : string.Empty);
    public bool ServiceFailed => _serviceState == "Vm.Failed";

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        { return; }
        (_serviceState, _serviceError, _stopping) = ("Vm.Starting", string.Empty, false);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            _child = (launcher ?? new VmProcess()).Start(host!.Executable, Address, Port, Processes.Count > 1 ? SelectedProcess : null);
            Changed();
            Task<VmEndpoint> listening = _child.ListeningAsync(limit.Token);
            Task first = await Task.WhenAny(listening, _child.Completion).ConfigureAwait(true);
            if (first == listening && !_stopping)
            {
                _bound = await listening.ConfigureAwait(true);
                _serviceState = "Vm.Listening";
                Changed();
            }

            string errors = await _child.Completion.ConfigureAwait(true);
            _serviceError = _stopping ? string.Empty : PlainError(errors);
            _serviceState = _stopping ? "Vm.Stopped" : "Vm.Failed";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _serviceState = _stopping ? "Vm.Stopped" : "Vm.Failed";
            _serviceError = _stopping ? string.Empty : ex is OperationCanceledException ? Loc.Current["Vm.StartTimeout"] : PlainError(ex.Message);
        }
        finally
        {
            await limit.CancelAsync().ConfigureAwait(true);
            _child?.Dispose();
            _child = null;
            Changed();
        }
    }

    private string PlainError(string text) => string.Join(Environment.NewLine, Regex.Replace(_key is { } key ? text.Replace(key, "[redacted]", StringComparison.Ordinal) : text,
        "[A-Za-z0-9_-]{43,}", "[redacted]").Split('\n').Where(line => line.Length > 0).TakeLast(20).Select(line => ProjectText.Line(line)));
    private void StopService()
    {
        _stopping = true;
        _child?.Dispose();
        Changed();
    }

    public void Dispose() { _disposed = true; StopService(); }
    internal sealed record InstanceReport(int SchemaVersion, IReadOnlyList<int> Processes);
}
