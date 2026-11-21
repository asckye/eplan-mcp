using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Addin;

/// <summary>What starting EPLAN gave: the process, the variant and version it runs, and its Bridge's handshake.</summary>
internal sealed record EplanStart(int ProcessId, string Variant, string Version, HandshakeResult Handshake, double Seconds)
{
    /// <summary>The harmless windows closed while waiting for the add-in (ADR-0063 decision 6).</summary>
    public IReadOnlyList<ClosedWindow> ClosedWindows { get; init; } = [];
}

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
/// on a modal system messages window (A160, live 2026-11-14). It never closes or forces anything.
/// </summary>
internal sealed class EplanLifecycle(AddinMachine machine, IEplanProcesses processes, IBridgeProbe bridge, TimeProvider time, AddinTimings timings, bool pinned, bool closeHarmless = true)
{
    /// <summary>The longest wait for the Bridge after starting: EPLAN with its splash, licence check and add-ins.</summary>
    public static readonly TimeSpan StartWait = TimeSpan.FromSeconds(180);

    /// <summary>
    /// True when this Host serves one EPLAN process over HTTP (ADR-0018 decision 8): starting, closing or restarting
    /// EPLAN would leave that binding pointing at a process that is gone.
    /// </summary>
    public bool Pinned { get; } = pinned;

    /// <summary>Records the windows this lifecycle closed in the audit (ADR-0063 decision 3); none in tests.</summary>
    internal WindowWatch? Watch { get; init; }

    /// <summary><paramref name="closeHarmless"/>: whether listed windows are closed or only reported (<c>eplanWindowWatch</c>, ADR-0063).</summary>
    public static EplanLifecycle ForMachine(bool pinned, bool closeHarmless = true, WindowWatch? watch = null) => new(AddinMachine.FromWindows(), new EplanProcesses(),
        new BridgeProbe(new PipeBridgeDiscovery(), new PipeBridgeConnector()), TimeProvider.System, AddinTimings.Default with { Load = StartWait }, pinned, closeHarmless)
    {
        Watch = watch,
    };

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
        TimeSpan swept = TimeSpan.Zero;
        var closed = new List<ClosedWindow>();
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
                throw await TimedOutAsync(eplan, pid, chosen, closed, cancellationToken).ConfigureAwait(false);
            }

            // ADR-0063 decision 6: a harmless window such as the system messages window keeps the add-in from loading.
            if (elapsed - swept >= timings.Resend)
            {
                EplanCloser.Sweep(eplan, closed, closeHarmless);
                swept = elapsed;
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
                await RecordAsync(closed, eplan.Id, "eplan_session_start", cancellationToken).ConfigureAwait(false);
                return new EplanStart(eplan.Id, chosen, installation.Version, handshake, Math.Round(time.GetElapsedTime(started).TotalSeconds, 1)) { ClosedWindows = closed };
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
    /// Asks the EPLAN with <paramref name="processId"/> to close as its close button would and waits up to a minute,
    /// closing on the way the windows on the <see cref="HarmlessWindows"/> list such as the system messages window
    /// (ADR-0063). It never forces EPLAN: one that stays open behind another window is left running, and the result
    /// names that window.
    /// </summary>
    public async Task<EplanClose> CloseAsync(int processId, CancellationToken cancellationToken)
    {
        if (Pinned)
        {
            throw new EplanLifecycleException(ErrorReason.CapabilityMissing,
                "This EplanMcp serves one EPLAN process over HTTP and cannot close it; ask the engineer to close EPLAN in the virtual machine.");
        }

        using IStartedEplan eplan = processes.Attach(processId) ?? throw new EplanLifecycleException(ErrorReason.SessionNotSelected,
            $"No EPLAN with process {processId.ToString(CultureInfo.InvariantCulture)} runs in this Windows session; call eplan_session_status.");
        EplanClose close = await EplanCloser.CloseAsync(eplan, time, timings, cancellationToken, closeHarmless).ConfigureAwait(false);
        await RecordAsync(close.ClosedWindows, processId, "eplan_session_close", cancellationToken).ConfigureAwait(false);
        return close;
    }

    private Task RecordAsync(IReadOnlyList<ClosedWindow> closed, int processId, string trigger, CancellationToken cancellationToken) =>
        Watch is { } watch && closed.Count > 0 ? watch.RecordAsync(closed, processId, trigger, cancellationToken) : Task.CompletedTask;

    /// <summary>
    /// ADR-0063 decision 7: a start that timed out behind a window not on the list leaves EPLAN to the engineer and names
    /// the window; with nothing in the way, EPLAN is asked to close as its close button would, never forced, so that it
    /// does not block the next start.
    /// </summary>
    private async Task<EplanLifecycleException> TimedOutAsync(IStartedEplan eplan, string pid, string variant, List<ClosedWindow> closed, CancellationToken cancellationToken)
    {
        List<EplanWindow> inTheWay = EplanCloser.Sweep(eplan, closed, closeHarmless);
        await RecordAsync(closed, eplan.Id, "eplan_session_start", cancellationToken).ConfigureAwait(false);
        var metadata = new Dictionary<string, string> { ["process"] = pid, ["variant"] = variant };
        if (inTheWay.Count > 0)
        {
            metadata["windows"] = string.Join(", ", inTheWay);
            return new EplanLifecycleException(ErrorReason.Timeout,
                $"EPLAN started but its EplanMcp add-in did not answer in time; EPLAN shows {string.Join(", ", inTheWay)}. EPLAN was left running for the engineer to answer it.", metadata);
        }

        EplanClose close = await EplanCloser.CloseAsync(eplan, time, timings, cancellationToken, closeHarmless).ConfigureAwait(false);
        await RecordAsync(close.ClosedWindows, eplan.Id, "eplan_session_start", cancellationToken).ConfigureAwait(false);
        return new EplanLifecycleException(ErrorReason.Timeout, close.Closed
            ? "EPLAN started but its EplanMcp add-in did not answer in time, with no dialog in the way; EplanMcp closed that EPLAN so the next start is not blocked. Check that the add-in is registered for this variant (doctor)."
            : "EPLAN started but its EplanMcp add-in did not answer in time; asked to close, EPLAN stayed open. Ask the engineer to close it.", metadata);
    }

    private EplanInstallation? Newest() => EplanInstallation.Find(machine.ProgramFiles, machine.FileVersion)
        .Where(i => EplanVersions.Support(i.Version) != EplanSupport.Unsupported && i.Variants.Count > 0)
        .OrderByDescending(i => Version.TryParse(i.Version, out Version? v) ? v : new Version(0, 0))
        .FirstOrDefault();
}
