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

    /// <summary>The call did not finish in time. Retryable.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>A defect in EplanMcp; the correlation ID identifies the log entries.</summary>
    public const string Internal = "INTERNAL";
}
