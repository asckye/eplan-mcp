namespace EplanMcp.Host.Addin;

/// <summary>
/// The EPLAN processes install-addin looks at and starts. Production: <see cref="EplanProcesses"/>; tests fake EPLAN
/// completely, so no test ever starts EPLAN.exe.
/// </summary>
internal interface IEplanProcesses
{
    /// <summary>
    /// The process IDs of the EPLAN instances running in this Windows session. Which variant each runs cannot be told
    /// from outside: the registry entry EPLAN keeps for a running instance names the licence, not the variant
    /// (api-notes A1), so install-addin does not try.
    /// </summary>
    public IReadOnlyList<int> Running();

    /// <summary>Starts <paramref name="exe"/> with the raw <paramref name="arguments"/>; throws when it cannot.</summary>
    public IStartedEplan Start(string exe, string arguments);
}

/// <summary>An EPLAN install-addin started. Disposing it lets go of the process, which keeps running.</summary>
internal interface IStartedEplan : IDisposable
{
    public int Id { get; }

    public bool HasExited { get; }

    /// <summary>
    /// Forgets what was read about the process's windows. .NET's <c>Process</c> keeps the main window handle it first
    /// found, which early in EPLAN's start is the splash screen; a close request sent later without refreshing goes to
    /// that window and does nothing (live check, api-notes A23). Call it before every <see cref="CloseMainWindow"/>.
    /// </summary>
    public void Refresh();

    /// <summary>Asks EPLAN to close as the window's close button does; false when it has no enabled main window.</summary>
    public bool CloseMainWindow();
}
