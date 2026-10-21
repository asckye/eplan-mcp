using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Addin;

/// <summary>
/// <c>eplanmcp install-addin</c> and <c>uninstall-addin</c> (M5 design 4.2). Install registers the add-in next to this
/// eplanmcp.exe with EPLAN's own command line, once for each chosen variant (all installed ones by default) of each
/// EPLAN version in the supported range (ADR-0009). It refuses while any EPLAN runs; then per variant it starts the
/// command, waits for the Bridge pipe of the EPLAN it started, checks in the handshake that EPLAN loaded this DLL,
/// records the registration and closes that EPLAN as its close button would. Uninstall does the same with
/// <c>/unregister</c> for each recorded registration. It never forces EPLAN to close: an EPLAN that shows a sign-in or
/// licence dialog or a system messages window, or does not close, is left for the engineer. Exit code 1 when anything
/// failed. Never used in serve mode.
/// </summary>
internal sealed class AddinCommand
{
    private readonly AddinMachine _machine;
    private readonly AddinRecord _record;
    private readonly IEplanProcesses _processes;
    private readonly EplanLauncher _launcher;
    private readonly TimeProvider _time;
    private readonly AddinTimings _timings;

    /// <summary>For tests: a temporary machine and data root, fake EPLAN processes and Bridges, and short timings.</summary>
    internal AddinCommand(AddinMachine machine, HostDataLocations locations, IEplanProcesses processes, IBridgeProbe bridge, TimeProvider time, AddinTimings timings)
    {
        _machine = machine;
        _record = AddinRecord.For(locations);
        _processes = processes;
        _launcher = new EplanLauncher(processes, bridge, time, timings);
        _time = time;
        _timings = timings;
    }

    public static AddinCommand ForMachine() => new(
        AddinMachine.FromWindows(), HostDataLocations.FromEnvironment(), new EplanProcesses(),
        new BridgeProbe(new PipeBridgeDiscovery(), new PipeBridgeConnector()), TimeProvider.System, AddinTimings.Default);

    /// <summary>Runs the command; says on <paramref name="progress"/> (stderr) before each start of EPLAN, which can take minutes.</summary>
    public async Task<int> RunAsync(AddinAction action, IReadOnlyList<string> variants, TextWriter output, TextWriter progress, CultureInfo culture, bool json)
    {
        bool zh = culture.TwoLetterISOLanguageName == "zh";
        Action<AddinAction, AddinResult> starting = (step, r) => progress.WriteLine(AddinTexts.Starting(step, r, _timings.Load, zh));
        AddinReport report = action == AddinAction.Install
            ? await InstallAsync(variants, starting).ConfigureAwait(false)
            : await UninstallAsync(starting).ConfigureAwait(false);
        await output.WriteAsync(json ? AddinOutput.Json(report) + Environment.NewLine : AddinOutput.Text(report, culture)).ConfigureAwait(false);
        return report.Failed ? 1 : 0;
    }

    internal static string RegisterArguments(string variant, string dll) => $"/Variant:\"{variant}\" EplApiModuleAction /register:\"{dll}\"";

    internal static string UnregisterArguments(string variant) => $"/Variant:\"{variant}\" EplApiModuleAction /unregister:\"{AddinLocation.AssemblyName}\"";

    internal async Task<AddinReport> InstallAsync(IReadOnlyList<string> variants, Action<AddinAction, AddinResult>? starting = null)
    {
        if (AddinLocation.Find(_machine.HostDirectory, out IReadOnlyList<string> searched) is not { } addin)
        {
            return new(AddinAction.Install, null, new(AddinProblem.AddinNotFound, string.Join("; ", searched)), []);
        }

        try
        {
            _record.All(); // registering what cannot be recorded would leave it for nobody to undo
        }
        catch (Exception ex) when (ex is AddinException or IOException or UnauthorizedAccessException)
        {
            return new(AddinAction.Install, addin.Dll, Unreadable(ex), []);
        }

        // In installation order: a version outside the range as one skipped line, else each chosen installed variant.
        IReadOnlyList<EplanInstallation> installations = EplanInstallation.Find(_machine.ProgramFiles, _machine.FileVersion);
        var plan = new List<(EplanInstallation Installation, string? Variant)>();
        foreach (EplanInstallation installation in installations)
        {
            plan.AddRange(EplanVersions.Support(installation.Version) == EplanSupport.Unsupported
                ? [(installation, null)]
                : installation.Variants.Where(v => variants.Count == 0 || variants.Contains(v, StringComparer.OrdinalIgnoreCase)).Select(v => (installation, (string?)v)));
        }

        string[] chosen = [.. plan.Select(p => p.Variant).OfType<string>()];
        string[] missing = [.. variants.Where(v => !chosen.Contains(v, StringComparer.OrdinalIgnoreCase))];
        if (missing.Length > 0 || chosen.Length == 0)
        {
            string installed = string.Join(", ", installations.Where(i => EplanVersions.Support(i.Version) != EplanSupport.Unsupported)
                .SelectMany(i => i.Variants).Distinct(StringComparer.OrdinalIgnoreCase));
            return new(AddinAction.Install, addin.Dll, missing.Length > 0
                ? new(AddinProblem.VariantNotFound, string.Join(", ", missing), installed.Length > 0 ? installed : null)
                : new(AddinProblem.NoEplan, Path.Combine(_machine.ProgramFiles, "EPLAN"), installations.Count > 0 ? string.Join(", ", installations.Select(i => i.Version)) : null), []);
        }

        try
        {
            ZoneMark.RemoveAll(addin.UnblockFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(AddinAction.Install, addin.Dll, new(AddinProblem.UnblockFailed, ex.Message), []);
        }

        var results = new List<AddinResult>();
        foreach ((EplanInstallation installation, string? variant) in plan)
        {
            results.Add(variant is null
                ? new(installation.Exe, installation.Version, null, AddinOutcome.Skipped) { Notes = [AddinNote.UnsupportedVersion] }
                : await RegisterAsync(installation, variant, addin.Dll, starting).ConfigureAwait(false));
        }

        return new(AddinAction.Install, addin.Dll, null, results);
    }

    internal async Task<AddinReport> UninstallAsync(Action<AddinAction, AddinResult>? starting = null)
    {
        IReadOnlyList<RecordedAddin> recorded;
        try
        {
            recorded = _record.All();
        }
        catch (Exception ex) when (ex is AddinException or IOException or UnauthorizedAccessException)
        {
            return new(AddinAction.Uninstall, null, Unreadable(ex), []);
        }

        IReadOnlyList<EplanInstallation> installations = EplanInstallation.Find(_machine.ProgramFiles, _machine.FileVersion);
        var results = new List<AddinResult>();
        foreach (RecordedAddin addin in recorded)
        {
            results.Add(await UnregisterAsync(addin, installations, starting).ConfigureAwait(false));
        }

        return new(AddinAction.Uninstall, null, null, results);
    }

    /// <summary>
    /// Whether the Bridge that answered was loaded from <paramref name="dll"/>: EPLAN passed the add-in that DLL, or its
    /// folder, since which of the two it passes is not verified (api-notes B2).
    /// </summary>
    internal static bool Loads(HandshakeResult? handshake, string dll) =>
        handshake?.AddinPath is { Length: > 0 } path && (Same(path, dll) || Same(path, Path.GetDirectoryName(dll)!));

    /// <summary>
    /// The pipe of the EPLAN the command started proves only that some registration loaded: one made earlier loads at
    /// start-up, and the live check found that <c>/register</c> of a new path then does not take (api-notes A23). So the
    /// handshake must name this DLL. When it names another path, or none (an older add-in), the earlier registration is
    /// undone in an EPLAN of its own and this one made again in a third, where nothing loads at start-up, so the pipe
    /// can come only from this registration. Whether that makes the new path take is not verified yet (design 4.2).
    /// </summary>
    private async Task<AddinResult> RegisterAsync(EplanInstallation installation, string variant, string dll, Action<AddinAction, AddinResult>? starting)
    {
        var result = new AddinResult(installation.Exe, installation.Version, variant, AddinOutcome.Refused)
        {
            Notes = EplanVersions.Support(installation.Version) == EplanSupport.Expected ? [AddinNote.VersionUntested] : [],
        };
        if (Running() is { } running)
        {
            return result with { Problem = running };
        }

        // The record is written before EPLAN is asked to close, so that a run cut short still leaves it to undo.
        bool ours = false;
        AddinIssue? recordIssue = null;
        void Record() => recordIssue = Update(() => _record.Add(new(installation.Exe, installation.Version, variant, dll, _time.GetUtcNow())));

        // Not loaded in time: recorded all the same, since it may take once the engineer is through a dialog.
        AddinResult Unfinished(Launch launch, AddinIssue problem)
        {
            if (problem.Problem != AddinProblem.NotLoaded)
            {
                return launch.Result with { Outcome = AddinOutcome.Failed, Problem = problem };
            }

            Record();
            return launch.Result with { Outcome = AddinOutcome.NotConfirmed, Problem = recordIssue ?? problem };
        }

        starting?.Invoke(AddinAction.Install, result);
        Launch first = await _launcher.RunAsync(result, RegisterArguments(variant, dll), TimeSpan.Zero, h =>
        {
            if (Loads(h, dll))
            {
                ours = true;
                Record();
            }
        }).ConfigureAwait(false);
        if (first.Problem is { } problem)
        {
            return Unfinished(first, problem);
        }

        if (ours)
        {
            return first.Result with { Outcome = AddinOutcome.Registered, Problem = recordIssue };
        }

        if (first.Handshake is null)
        {
            // Which registration loaded cannot be told: undoing it might undo another user's (Q15). Recorded, since
            // this one may have taken, and uninstall-addin should undo it.
            Record();
            return first.Result with { Outcome = AddinOutcome.NotConfirmed, Problem = recordIssue ?? HandshakeFailed(first) };
        }

        string loaded = first.Handshake.AddinPath ?? "?";
        if (!first.Closed)
        {
            return first.Result with { Outcome = AddinOutcome.NotConfirmed, Problem = new(AddinProblem.PathMismatch, loaded) };
        }

        starting?.Invoke(AddinAction.Uninstall, result);
        Launch undo = await _launcher.RunAsync(result, UnregisterArguments(variant), _timings.Settle, _ => { }).ConfigureAwait(false);
        if (undo.Problem is { } undoProblem)
        {
            return undo.Result with { Outcome = undoProblem.Problem == AddinProblem.NotLoaded ? AddinOutcome.NotConfirmed : AddinOutcome.Failed, Problem = undoProblem };
        }

        if (!undo.Closed)
        {
            return undo.Result with { Outcome = AddinOutcome.NotConfirmed, Problem = new(AddinProblem.PathMismatch, loaded) };
        }

        // Nothing is registered now, so whatever loads in this start is this registration, saying its path or not.
        starting?.Invoke(AddinAction.Install, result);
        Launch again = await _launcher.RunAsync(result, RegisterArguments(variant, dll), TimeSpan.Zero, h =>
        {
            if (h is not null && (h.AddinPath is null || Loads(h, dll)))
            {
                ours = true;
                Record();
            }
        }).ConfigureAwait(false);
        if (again.Problem is { Problem: AddinProblem.EplanExited or AddinProblem.StartFailed } gone)
        {
            // The earlier registration is undone and this one did not get made: nothing is left to undo.
            return again.Result with { Outcome = AddinOutcome.Failed, Problem = Update(() => _record.Remove(installation.Exe, variant)) ?? gone };
        }

        if (again.Problem is { } againProblem)
        {
            return Unfinished(again, againProblem);
        }

        if (ours)
        {
            return again.Result with { Outcome = AddinOutcome.Registered, Problem = recordIssue };
        }

        if (again.Handshake is null)
        {
            Record(); // an add-in did load where nothing was registered
            return again.Result with { Outcome = AddinOutcome.NotConfirmed, Problem = recordIssue ?? HandshakeFailed(again) };
        }

        return again.Result with { Outcome = AddinOutcome.NotConfirmed, Problem = new(AddinProblem.PathMismatch, again.Handshake.AddinPath ?? "?") };
    }

    private static AddinIssue HandshakeFailed(Launch launch) =>
        new(AddinProblem.HandshakeFailed, launch.Result.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "?");
    /// <summary>
    /// Runs only an EPLAN.exe and variant that are installed now, never a program or variant named only by the record:
    /// a record whose EPLAN or variant is gone has nothing left to undo and is dropped. The add-in loads before EPLAN
    /// runs <c>/unregister</c> (api-notes A23), so its pipe shows that EPLAN is up; the record goes after
    /// <see cref="AddinTimings.Settle"/>, and is kept when not confirmed, so that running uninstall-addin again tries
    /// once more.
    /// </summary>
    private async Task<AddinResult> UnregisterAsync(RecordedAddin addin, IReadOnlyList<EplanInstallation> installations, Action<AddinAction, AddinResult>? starting)
    {
        var result = new AddinResult(addin.Eplan, addin.Version, addin.Variant, AddinOutcome.Refused);
        string? variant = installations.Where(i => i.Exe.Equals(addin.Eplan, StringComparison.OrdinalIgnoreCase))
            .SelectMany(i => i.Variants).FirstOrDefault(v => v.Equals(addin.Variant, StringComparison.OrdinalIgnoreCase));
        if (variant is null)
        {
            return result with { Outcome = AddinOutcome.Skipped, Notes = [AddinNote.EplanMissing], Problem = Update(() => _record.Remove(addin.Eplan, addin.Variant)) };
        }

        if (Running() is { } running)
        {
            return result with { Problem = running };
        }

        starting?.Invoke(AddinAction.Uninstall, result);
        AddinIssue? removed = null;
        Launch launch = await _launcher.RunAsync(result, UnregisterArguments(variant), _timings.Settle, _ => removed = Update(() => _record.Remove(addin.Eplan, addin.Variant))).ConfigureAwait(false);
        return launch.Problem is { } problem
            ? launch.Result with { Outcome = problem.Problem == AddinProblem.NotLoaded ? AddinOutcome.NotConfirmed : AddinOutcome.Failed, Problem = problem }
            : launch.Result with { Outcome = AddinOutcome.Unregistered, Problem = removed };
    }

    /// <summary>
    /// Any EPLAN running in this session: the command would start a second instance with every add-in (api-notes A23),
    /// and which variant a running EPLAN is cannot be told from outside (A1), so any of them is in the way.
    /// </summary>
    private AddinIssue? Running()
    {
        string[] running = [.. _processes.Running().Select(p => p.ToString(CultureInfo.InvariantCulture))];
        return running.Length == 0 ? null : new(AddinProblem.EplanRunning, string.Join(", ", running));
    }

    private static bool Same(string path, string other) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(other)), StringComparison.OrdinalIgnoreCase);
    private static AddinIssue Unreadable(Exception ex) => ex is AddinException invalid ? new(invalid.Problem, invalid.Detail) : new(AddinProblem.InvalidRecord, ex.Message);

    /// <summary>Null when the record follows what was done, else why it could not.</summary>
    private static AddinIssue? Update(Action update)
    {
        try
        {
            update();
            return null;
        }
        catch (Exception ex) when (ex is AddinException or IOException or UnauthorizedAccessException)
        {
            return new(AddinProblem.RecordFailed, ex is AddinException refused ? refused.Detail : ex.Message);
        }
    }
}
