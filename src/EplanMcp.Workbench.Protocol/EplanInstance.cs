namespace EplanMcp.Workbench.Protocol;

/// <summary>The EPLAN process a question's project is open in.</summary>
/// <param name="Version">The file version of EPLAN.exe, such as <c>2026.0.3.25421</c>.</param>
/// <param name="Variant">The variant, such as <c>Pro Panel</c>; null when unknown.</param>
/// <param name="ProcessId">The process ID of EPLAN.exe.</param>
public sealed record EplanInstance(string Version, string? Variant, int ProcessId);
