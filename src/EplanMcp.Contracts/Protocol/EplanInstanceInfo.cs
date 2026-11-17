namespace EplanMcp.Contracts;

/// <summary>Facts about the EPLAN process hosting a Bridge; the API extension licence is checked on each handshake.</summary>
public sealed class EplanInstanceInfo
{
    /// <summary>Windows process ID of the process EPLAN runs in: EPLAN.exe, or the offline host.</summary>
    public int ProcessId { get; set; }

    /// <summary>File version of EPLAN.exe, for example <c>2026.0.3.25421</c>.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Product variant such as <c>Electric P8</c> or <c>Pro Panel</c>; null when it could not be determined.</summary>
    public string? Variant { get; set; }

    /// <summary>Whether the API extension licence option is available (<c>LicenseOptions.APIExtension</c>); null if not checked.</summary>
    public bool? ApiExtensionLicence { get; set; }

    /// <summary>
    /// True when EPLAN runs without a user interface in the offline host (M5 design 4.9): it cannot show the approval
    /// dialog, so the Bridge offers no <c>approval.ask</c>, and the Host asks nobody, not even the Workbench, about a
    /// write through it: only scratch can be written. False in the add-in. Added without a protocol version change;
    /// older add-ins send none, which reads as false.
    /// </summary>
    public bool Offline { get; set; }
}
