namespace EplanMcp.Host.Doctor;

internal enum DoctorStatus
{
    Ok,
    Info,
    Warning,
    Failure,
}

/// <summary>One line of <c>eplanmcp doctor</c> output, in both languages (architecture 8.11).</summary>
internal sealed record DoctorCheck(DoctorStatus Status, string Chinese, string English);
