namespace EplanMcp.Workbench.Commands;

// The JSON the Host's commands write for the Workbench (architecture 5.2, M5 design 4.11), schemaVersion 1, field by
// field: HostCommands reads it strictly, so anything else is an error (ERR-08). The snapshots in
// tests/Contracts.Snapshots are the contract; the Workbench tests read them with these types.

/// <summary>A check's or a whole report's status; <see cref="Info"/> counts as <see cref="Ok"/> in the report's.</summary>
internal enum CheckStatus
{
    Ok,
    Info,
    Warning,
    Failure,
}

/// <summary>A text of the Host's in both languages, with a stable code (problems and notes).</summary>
internal sealed record HostText(string Code, string Zh, string En);

internal sealed record AddinVariants(int SchemaVersion, IReadOnlyList<string> Variants);

internal enum AddinAction
{
    Install,
    Uninstall,
}

internal enum AddinResultKind
{
    Registered,
    Unregistered,
    NotConfirmed,
    Failed,
    Refused,
    Skipped,
}

internal sealed record AddinReport(int SchemaVersion, AddinAction Action, string? Dll, HostText? Problem, IReadOnlyList<AddinResult> Results);

internal sealed record AddinResult(string Eplan, string Version, string? Variant, AddinResultKind Result, int? ProcessId, IReadOnlyList<HostText> Notes, HostText? Problem);

/// <summary><c>eplanmcp doctor --format json</c>.</summary>
internal sealed record DoctorReport(int SchemaVersion, string HostVersion, CheckStatus Status, IReadOnlyList<DoctorCheck> Checks);

/// <summary>
/// One check: <see cref="Id"/> is stable, such as <c>bridge.licence</c>, and shared by the checks of one kind;
/// <see cref="Subject"/> is the EPLAN version or process ID the check is about, when it is about one.
/// </summary>
internal sealed record DoctorCheck(string Id, CheckStatus Status, string Zh, string En, string? Subject = null);

/// <summary><c>eplanmcp audit verify --format json</c>; <see cref="IntactRecords"/> counts the records before the break, or all.</summary>
internal sealed record AuditVerifyReport(int SchemaVersion, string Directory, bool Exists, int IntactRecords, bool Intact, AuditBreak? Break);

internal enum AuditBreakProblem
{
    Changed,
    Missing,
    NotARecord,
}

/// <summary>Where the hash chain breaks first: the file and its line, counted from 1.</summary>
internal sealed record AuditBreak(string File, int Line, AuditBreakProblem Problem);

internal enum ClientConfigMode
{
    Write,
    DryRun,
    Remove,
}

internal enum ClientAction
{
    Write,
    Remove,
}

internal enum ClientResultKind
{
    Written,
    WouldWrite,
    Removed,
    Unchanged,
    Failed,
}

/// <summary><c>eplanmcp client-config … --format json</c>; <see cref="Problem"/> when the whole command was refused.</summary>
internal sealed record ClientConfigReport(int SchemaVersion, ClientConfigMode Mode, ConfiguredServer Server, HostText? Problem, IReadOnlyList<ClientOutcome> Clients);

/// <summary>The entry the Host writes: the server's name and how a client starts it.</summary>
internal sealed record ConfiguredServer(string Name, string Command, IReadOnlyList<string> Args);

/// <summary>
/// One client's part. <see cref="Before"/> and <see cref="After"/> are only the <c>eplan</c> entry as the file has it
/// (JSON or TOML text), never the rest of the file; <see cref="File"/> is null when its place cannot be told.
/// </summary>
internal sealed record ClientOutcome(
    string Client,
    string? File,
    ClientAction Action,
    ClientResultKind Result,
    string? Before,
    string? After,
    string? Backup,
    IReadOnlyList<HostText> Notes,
    HostText? Problem);
