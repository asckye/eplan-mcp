namespace EplanMcp.Host.Addin;

internal enum AddinAction
{
    Install,
    Uninstall,
}

internal enum AddinOutcome
{
    /// <summary>The started EPLAN loaded the add-in.</summary>
    Registered,

    /// <summary>The started EPLAN came up and ran <c>/unregister</c>.</summary>
    Unregistered,

    /// <summary>The command ran, but EPLAN was still starting when the wait ended; it was left open.</summary>
    NotConfirmed,

    Failed,

    /// <summary>Nothing was started: EPLAN of that variant runs, or another user's registration is in the way.</summary>
    Refused,

    /// <summary>Nothing to do here, see the notes.</summary>
    Skipped,
}

/// <summary>
/// One EPLAN.exe and variant. <see cref="Variant"/> is null for a version skipped as a whole; <see cref="ProcessId"/> is
/// the EPLAN the command started.
/// </summary>
internal sealed record AddinResult(string Eplan, string Version, string? Variant, AddinOutcome Outcome)
{
    public int? ProcessId { get; init; }

    public AddinIssue? Problem { get; init; }

    public IReadOnlyList<AddinNote> Notes { get; init; } = [];
}

/// <summary>A whole run: the add-in DLL (install only), a problem that stopped everything, and each result.</summary>
internal sealed record AddinReport(AddinAction Action, string? Dll, AddinIssue? Problem, IReadOnlyList<AddinResult> Results)
{
    public bool Failed => Problem is not null || Results.Any(r => r.Problem is not null);
}
