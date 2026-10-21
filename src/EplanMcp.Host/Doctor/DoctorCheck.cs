namespace EplanMcp.Host.Doctor;

internal enum DoctorStatus
{
    Ok,
    Info,
    Warning,
    Failure,
}

/// <summary>
/// One line of <c>eplanmcp doctor</c> output, in both languages (architecture 8.11). <see cref="Id"/> is one of
/// <see cref="DoctorCheckIds"/>, shared by the checks of one kind; <see cref="Subject"/> is the EPLAN version (of an
/// installation) or process ID the check is about, when it is about one.
/// </summary>
internal sealed record DoctorCheck(string Id, DoctorStatus Status, string Chinese, string English, string? Subject = null);
