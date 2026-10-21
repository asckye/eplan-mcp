namespace EplanMcp.Host.Addin;

/// <summary>Why install-addin or uninstall-addin did not do, or could not confirm, what it was asked to.</summary>
internal enum AddinProblem
{
    /// <summary>No add-in DLL next to this eplanmcp.exe, installed or built.</summary>
    AddinNotFound,

    /// <summary>No EPLAN, or none in the supported range with an installed variant.</summary>
    NoEplan,

    /// <summary>A variant asked for is installed in no supported EPLAN version.</summary>
    VariantNotFound,

    /// <summary>The download mark could not be removed from the add-in's files.</summary>
    UnblockFailed,

    /// <summary><c>state\addins.json</c> cannot be read.</summary>
    InvalidRecord,

    /// <summary>EPLAN runs in this session; which variant cannot be told (api-notes A1), so any EPLAN is in the way.</summary>
    EplanRunning,

    /// <summary>EPLAN.exe could not be started.</summary>
    StartFailed,

    /// <summary>The started EPLAN exited before the add-in loaded.</summary>
    EplanExited,

    /// <summary>The started EPLAN did not load the add-in in time and was left open.</summary>
    NotLoaded,

    /// <summary>The started EPLAN loaded the add-in from another path, or from one an older add-in does not report (an empty detail).</summary>
    PathMismatch,

    /// <summary>The Bridge of the started EPLAN did not answer the handshake, so which add-in loaded cannot be told.</summary>
    HandshakeFailed,

    /// <summary>The registration was made or undone, but the record could not be updated.</summary>
    RecordFailed,
}

/// <summary>A remark on a result that is not a failure.</summary>
internal enum AddinNote
{
    /// <summary>The version is in the supported range but not tested yet (ADR-0009).</summary>
    VersionUntested,

    /// <summary>The version is outside the supported range, so nothing was registered there (ADR-0009).</summary>
    UnsupportedVersion,

    /// <summary>The recorded EPLAN.exe is gone, so there is nothing left to unregister; the record is dropped.</summary>
    EplanMissing,

    /// <summary>The started EPLAN did not close when asked; the engineer closes it.</summary>
    CloseEplan,
}

/// <summary>A problem and what was found, in English (paths, process IDs, messages); <see cref="Installed"/> lists the installed variants for <see cref="AddinProblem.VariantNotFound"/>.</summary>
internal sealed record AddinIssue(AddinProblem Problem, string Detail, string? Installed = null);

internal sealed class AddinException : Exception
{
    public AddinException()
    {
    }

    public AddinException(string message)
        : base(message)
    {
    }

    public AddinException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AddinException(AddinProblem problem, string detail, Exception? innerException = null)
        : base($"{problem}: {detail}", innerException)
    {
        Problem = problem;
        Detail = detail;
    }

    public AddinProblem Problem { get; }

    public string Detail { get; } = string.Empty;
}
