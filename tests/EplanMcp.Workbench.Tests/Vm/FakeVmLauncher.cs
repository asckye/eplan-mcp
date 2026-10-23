using EplanMcp.Workbench.Vm;

namespace EplanMcp.Workbench.Tests.Vm;

internal sealed class FakeVmLauncher : IVmLauncher
{
    public List<(string Executable, string Address, string Port, int? Process)> Starts { get; } = [];
    public FakeChild Child { get; } = new();
    public Exception? Error { get; set; }
    public IVmChild Start(string executable, string address, string port, int? processId)
    {
        Starts.Add((executable, address, port, processId));
        if (Error is { } error)
        { throw error; }
        return Child;
    }

    internal sealed class FakeChild : IVmChild
    {
        public TaskCompletionSource<VmEndpoint> Listening { get; } = new();
        public TaskCompletionSource<string> Exit { get; } = new();
        public bool Stopped { get; private set; }
        public Task<string> Completion => Exit.Task;
        public Task<VmEndpoint> ListeningAsync(CancellationToken cancellationToken) => Listening.Task.WaitAsync(cancellationToken);
        public void Dispose()
        {
            Stopped = true;
            Exit.TrySetResult(string.Empty);
        }
    }
}
