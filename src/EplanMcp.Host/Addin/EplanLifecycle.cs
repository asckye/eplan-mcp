using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Addin;

/// <summary>What starting EPLAN gave: the process, the variant and version it runs, and its Bridge's handshake.</summary>
internal sealed record EplanStart(int ProcessId, string Variant, string Version, HandshakeResult Handshake, double Seconds);

/// <summary>Why starting or closing EPLAN did not happen as asked; maps onto an MCP error reason.</summary>
internal sealed class EplanLifecycleException(string reason, string message, IReadOnlyDictionary<string, string>? metadata = null) : Exception(message)
{
    public string Reason { get; } = reason;

    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata ?? new Dictionary<string, string>();
}

/// <summary>
/// Starts EPLAN for eplan_session_start (ADR-0058, docs/tools/eplan_session_start.md) the way install-addin does: the
/// executable doctor finds, the variant on the command line, then the Bridge pipe of the process it started and a
/// handshake. It refuses while any EPLAN runs: a second instance fights the first over the station settings and stops
/// on a modal system messages window (live 2026-11-14). It never closes or forces anything.
/// </summary>
internal sealed class EplanLifecycle(AddinMachine machine, IEplanProcesses processes, IBridgeProbe bridge, TimeProvider time, AddinTimings timings, bool pinned)
{
    /// <summary>The longest wait for the Bridge after starting: EPLAN with its splash, licence check and add-ins.</summary>
    public static readonly TimeSpan StartWait = TimeSpan.FromSeconds(180);

    /// <summary>
    /// True when this Host serves one EPLAN process over HTTP (ADR-0018 decision 8): starting, closing or restarting
    /// EPLAN would leave that binding pointing at a process that is gone.
    /// </summary>
    public bool Pinned { get; } = pinned;

    public static EplanLifecycle ForMachine(bool pinned) => new(AddinMachine.FromWindows(), new EplanProcesses(),
        new BridgeProbe(new PipeBridgeDiscovery(), new PipeBridgeConnector()), TimeProvider.System, AddinTimings.Default with { Load = StartWait }, pinned);

    /// <summary>The variants of the newest supported installation, which <see cref="StartAsync"/> starts from.</summary>
    public IReadOnlyList<string> Variants() => Newest()?.Variants ?? [];

    /// <summary>Starts <paramref name="variant"/> (Electric P8 when installed and none is given) and waits for its Bridge.</summary>
    public async Task<EplanStart> StartAsync(string? variant, Action<string>? progress, CancellationToken cancellationToken)
    {
        if (Pinned)
        {
            throw new EplanLifecycleException(ErrorReason.CapabilityMissing,
                "This EplanMcp serves one EPLAN process over HTTP and cannot start another; start EPLAN in the virtual machine and restart the service there.");
        }

        EplanInstallation installation = Newest()
            ?? throw new EplanLifecycleException(ErrorReason.CapabilityMissing, "No supported EPLAN is installed on this computer (doctor lists what it finds).");
        string chosen = string.IsNullOrWhiteSpace(variant)
            ? installation.Variants.FirstOrDefault(v => v.Equals("Electric P8", StringComparison.OrdinalIgnoreCase)) ?? installation.Variants[0]
            : installation.Variants.FirstOrDefault(v => v.Equals(variant.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new EplanLifecycleException(ErrorReason.InvalidArgument, $"'variant' must be one of: {string.Join(", ", installation.Variants)}.");

        IReadOnlyList<int> running = processes.Running();
        if (running.Count > 0)
        {
            throw new EplanLifecycleException(ErrorReason.AlreadyExists,
                "EPLAN is already running; a second instance would fight it over the station settings. Call eplan_session_status, or close EPLAN first.",
                new Dictionary<string, string> { ["processes"] = string.Join(", ", running.Select(id => id.ToString(CultureInfo.InvariantCulture))) });
        }

        long started = time.GetTimestamp();
        using IStartedEplan eplan = processes.Start(installation.Exe, $"/Variant:\"{chosen}\"");
        string pid = eplan.Id.ToString(CultureInfo.InvariantCulture);
        TimeSpan reported = TimeSpan.Zero;
        while (!bridge.IsUp(eplan.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (eplan.HasExited)
            {
                throw new EplanLifecycleException(ErrorReason.EplanNotConnected, "EPLAN exited while starting; ask the engineer to start it by hand and look at what it says.",
                    new Dictionary<string, string> { ["process"] = pid });
            }

            TimeSpan elapsed = time.GetElapsedTime(started);
            if (elapsed >= timings.Load)
            {
                throw new EplanLifecycleException(ErrorReason.Timeout,
                    "EPLAN started but its EplanMcp add-in did not answer in time; it may be waiting on a dialog, or the add-in is not registered for this variant. EPLAN was left running.",
                    new Dictionary<string, string> { ["process"] = pid, ["variant"] = chosen });
            }

            if (elapsed - reported >= TimeSpan.FromSeconds(5))
            {
                progress?.Invoke(string.Create(CultureInfo.InvariantCulture, $"Waiting for EPLAN {pid} ({elapsed.TotalSeconds:0} s)"));
                reported = elapsed;
            }

            await Task.Delay(timings.Poll, time, cancellationToken).ConfigureAwait(false);
        }

        for (int i = 1; ; i++)
        {
            if (await bridge.HandshakeAsync(eplan.Id, cancellationToken).ConfigureAwait(false) is { } handshake)
            {
                return new EplanStart(eplan.Id, chosen, installation.Version, handshake, Math.Round(time.GetElapsedTime(started).TotalSeconds, 1));
            }

            if (i == EplanLauncher.HandshakeTries)
            {
                throw new EplanLifecycleException(ErrorReason.BridgeIncompatible, "EPLAN started and its add-in pipe is up, but the add-in did not answer the handshake. Call eplan_session_status.",
                    new Dictionary<string, string> { ["process"] = pid });
            }

            await Task.Delay(timings.Poll, time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks the EPLAN with <paramref name="processId"/> to close as its close button would and waits up to a minute; true
    /// when it exited. It never forces EPLAN: one that stays open (a dialog, a system messages window) is left running.
    /// </summary>
    public async Task<bool> CloseAsync(int processId, CancellationToken cancellationToken)
    {
        if (Pinned)
        {
            throw new EplanLifecycleException(ErrorReason.CapabilityMissing,
                "This EplanMcp serves one EPLAN process over HTTP and cannot close it; ask the engineer to close EPLAN in the virtual machine.");
        }

        using IStartedEplan eplan = processes.Attach(processId) ?? throw new EplanLifecycleException(ErrorReason.SessionNotSelected,
            $"No EPLAN with process {processId.ToString(CultureInfo.InvariantCulture)} runs in this Windows session; call eplan_session_status.");
        return await EplanCloser.CloseAsync(eplan, time, timings, cancellationToken).ConfigureAwait(false);
    }

    private EplanInstallation? Newest() => EplanInstallation.Find(machine.ProgramFiles, machine.FileVersion)
        .Where(i => EplanVersions.Support(i.Version) != EplanSupport.Unsupported && i.Variants.Count > 0)
        .OrderByDescending(i => Version.TryParse(i.Version, out Version? v) ? v : new Version(0, 0))
        .FirstOrDefault();
}
