using System.Runtime.InteropServices;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// One Workbench per Windows user and data root (ADR-0014 decision 1, M5 design 4.4): a named mutex in the session's
/// namespace marks the first; a second start signals the first's event, letting it come to the front, and exits.
/// The names carry the pipe name's digest of user SID and data root, so other data roots run their own Workbench.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>Lets any process take the foreground once: the second start hands its right to the first.</summary>
    private const int AnyProcess = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _listening;

    /// <param name="name">The pipe name of this user and data root (<c>WorkbenchPipeName.ForCurrentUser</c>).</param>
    public SingleInstance(string name)
    {
        // Held, never owned: while this handle is open, the next start finds the mutex there.
        _mutex = new Mutex(initiallyOwned: false, @"Local\" + name + ".Instance", out bool created);
        IsFirst = created;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".Activate");
    }

    public bool IsFirst { get; }

    /// <summary>The second start's part: asks the first to come to the front.</summary>
    public void SignalFirst()
    {
        AllowSetForegroundWindow(AnyProcess);
        _activate.Set();
    }

    /// <summary>The first start's part: <paramref name="activate"/> runs, on a pool thread, for every later start.</summary>
    public void Listen(Action activate) =>
        _listening = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => activate(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _listening?.Unregister(null);
        _activate.Dispose();
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
