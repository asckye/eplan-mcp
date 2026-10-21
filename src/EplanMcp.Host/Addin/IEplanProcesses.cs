namespace EplanMcp.Host.Addin;

/// <summary>A running EPLAN in this Windows session; <see cref="Variant"/> is null when it cannot be told.</summary>
internal sealed record RunningEplan(int ProcessId, string? Variant);

/// <summary>
/// The EPLAN processes install-addin looks at and starts. Production: <see cref="EplanProcesses"/>; tests fake EPLAN
/// completely, so no test ever starts EPLAN.exe.
/// </summary>
internal interface IEplanProcesses
{
    public IReadOnlyList<RunningEplan> Running();

    /// <summary>Starts <paramref name="exe"/> with the raw <paramref name="arguments"/>; throws when it cannot.</summary>
    public IStartedEplan Start(string exe, string arguments);
}

/// <summary>An EPLAN install-addin started. Disposing it lets go of the process, which keeps running.</summary>
internal interface IStartedEplan : IDisposable
{
    public int Id { get; }

    public bool HasExited { get; }

    /// <summary>Asks EPLAN to close as the window's close button does; false when it has no enabled main window.</summary>
    public bool CloseMainWindow();
}
