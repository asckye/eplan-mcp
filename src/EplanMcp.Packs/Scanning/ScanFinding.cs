namespace EplanMcp.Packs.Scanning;

/// <summary>
/// One thing the scanner found. <see cref="Code"/> is a stable snake_case name (such as <c>missing_data_folder</c>);
/// <see cref="Subject"/> is the input's name and the path below it, never a local absolute path.
/// </summary>
public sealed record ScanFinding(FindingSeverity Severity, string Code, string Subject, string Message);
