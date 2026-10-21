namespace EplanMcp.Workbench.Protocol;

/// <summary>The write of unknown outcome that suspended writes (ADR-0010 section 3), for a release question.</summary>
/// <param name="RequestId">The request ID of that write.</param>
/// <param name="Tool">Its tool.</param>
/// <param name="TimeUnixMs">When it was suspended, in Unix milliseconds (UTC).</param>
/// <param name="Project">The suspended project's full path; null when every project of the instance is suspended.</param>
/// <param name="ProcessId">The suspended EPLAN instance's process ID; null when one project is suspended.</param>
public sealed record SuspensionRecord(string RequestId, string Tool, long TimeUnixMs, string? Project, int? ProcessId);
