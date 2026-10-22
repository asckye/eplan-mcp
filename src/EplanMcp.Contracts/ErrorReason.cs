namespace EplanMcp.Contracts;

/// <summary>
/// Error reasons shared by the Bridge and the Host, as listed in docs/standards/mcp-tool-design.md, section 6.3.
/// Add a reason there before adding it here; never use string literals for reasons elsewhere in the code.
/// </summary>
public static class ErrorReason
{
    /// <summary>An argument is invalid; <c>metadata</c> names each problem.</summary>
    public const string InvalidArgument = "INVALID_ARGUMENT";

    /// <summary>The named project is not open in EPLAN.</summary>
    public const string ProjectNotOpen = "PROJECT_NOT_OPEN";

    /// <summary>
    /// A name matches several objects (a short device tag in several structures, two projects with one name);
    /// <c>metadata.candidates</c> lists their full names, one per line, at most 20.
    /// </summary>
    public const string AmbiguousName = "AMBIGUOUS_NAME";

    /// <summary>No page has the given name; <c>metadata.page</c> repeats it, and eplan_page_list shows the page names.</summary>
    public const string PageNotFound = "PAGE_NOT_FOUND";

    /// <summary>No device has the given tag; eplan_device_list finds the full tag.</summary>
    public const string DeviceNotFound = "DEVICE_NOT_FOUND";

    /// <summary>No window macro has the given path below the macro directory; <c>metadata.macro</c> repeats it, and eplan_macro_list shows the paths.</summary>
    public const string MacroNotFound = "MACRO_NOT_FOUND";

    /// <summary>EPLAN's parts database has no part with the given number; eplan_part_search finds part numbers.</summary>
    public const string PartNotFound = "PART_NOT_FOUND";

    /// <summary>No file has the given path below the import folder (SEC-31); <c>metadata.file</c> repeats it.</summary>
    public const string FileNotFound = "FILE_NOT_FOUND";

    /// <summary>Several EPLAN instances are running and none is selected.</summary>
    public const string SessionNotSelected = "SESSION_NOT_SELECTED";

    /// <summary>No running EPLAN with the add-in loaded was found. Retryable.</summary>
    public const string EplanNotConnected = "EPLAN_NOT_CONNECTED";

    /// <summary>EPLAN's main thread was not available in time, or a modal dialog is open. Retryable.</summary>
    public const string EplanBusy = "EPLAN_BUSY";

    /// <summary>The add-in and the Host speak incompatible protocol versions.</summary>
    public const string BridgeIncompatible = "BRIDGE_INCOMPATIBLE";

    /// <summary>The API extension licence is missing or expired.</summary>
    public const string LicenceMissing = "LICENCE_MISSING";

    /// <summary>The licence exists but restricts this use (ADR-0007).</summary>
    public const string LicenceRestricted = "LICENCE_RESTRICTED";

    /// <summary>EPLAN objects are locked by another user. Retryable.</summary>
    public const string LockFailed = "LOCK_FAILED";

    /// <summary>EPLAN reported a failure; any change was rolled back.</summary>
    public const string EplanOperationFailed = "EPLAN_OPERATION_FAILED";

    /// <summary>
    /// The call did not finish in time. Retryable. A write returns it only before its execution request reached the
    /// Bridge; after that the Host fetches the outcome instead (ADR-0010).
    /// </summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>An object with the new name exists already, for example a page or a device tag.</summary>
    public const string AlreadyExists = "ALREADY_EXISTS";

    /// <summary>The safety policy forbids the call; the message names the policy and never how to get around it.</summary>
    public const string PolicyDenied = "POLICY_DENIED";

    /// <summary>The engineer declined or cancelled the approval; nothing was changed.</summary>
    public const string ApprovalDenied = "APPROVAL_DENIED";

    /// <summary>The approval request expired; call again for a new one.</summary>
    public const string ApprovalExpired = "APPROVAL_EXPIRED";

    /// <summary>The planned objects changed after the plan was made; call again for a new plan.</summary>
    public const string PlanStale = "PLAN_STALE";

    /// <summary>The audit log cannot be written, so writes are refused.</summary>
    public const string AuditUnavailable = "AUDIT_UNAVAILABLE";

    /// <summary>
    /// A write was interrupted and its outcome cannot be fetched: whether the project changed is unknown. Never retry;
    /// writes to the project are suspended until the engineer has checked it (ADR-0010).
    /// </summary>
    public const string OutcomeUnknown = "OUTCOME_UNKNOWN";

    /// <summary>Writes to the project, or to the EPLAN instance, are suspended after an unknown outcome (ADR-0010).</summary>
    public const string WritesSuspended = "WRITES_SUSPENDED";

    /// <summary>
    /// The client cancelled the call, and the Bridge stopped it at a safe point before it committed anything (M4 design
    /// 4.3). The model never sees it, because a cancelled request gets no reply; outcome.get and the audit record it.
    /// </summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>
    /// A Destructive write was not run because the project could not be backed up first: the copy failed, took too long,
    /// or has another page count than the project (ADR-0013); or a write to system master data was not run because the
    /// database could not be backed up first (ADR-0019). Nothing was changed; never retried automatically.
    /// </summary>
    public const string BackupFailed = "BACKUP_FAILED";

    /// <summary>
    /// An engineering pack or development pack fails its check (ADR-0024; an official rule without a source, M6 design
    /// 4.2); <c>metadata.problems</c> lists every problem. Nothing was installed or built.
    /// </summary>
    public const string PackInvalid = "PACK_INVALID";

    /// <summary>No engineering pack, pack version or pack resource has the given name (M6 design 4.5.1).</summary>
    public const string ResourceNotFound = "RESOURCE_NOT_FOUND";

    /// <summary>What a pack or resource needs is not available now (M6 design 4.4); <c>metadata.missing</c> lists it.</summary>
    public const string CapabilityMissing = "CAPABILITY_MISSING";

    /// <summary>A configuration layer widens a hard constraint, or the effective default breaks one (M6 design 4.3).</summary>
    public const string ConfigConflict = "CONFIG_CONFLICT";

    /// <summary>A defect in EplanMcp; the correlation ID identifies the log entries.</summary>
    public const string Internal = "INTERNAL";
}
