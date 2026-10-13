namespace EplanMcp.Contracts;

/// <summary>Static facts about the EPLAN process hosting a Bridge, captured once when the add-in starts.</summary>
public sealed class EplanInstanceInfo
{
    /// <summary>Windows process ID of EPLAN.exe.</summary>
    public int ProcessId { get; set; }

    /// <summary>File version of EPLAN.exe, for example <c>2026.0.3.25421</c>.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Product variant such as <c>Electric P8</c> or <c>Pro Panel</c>; null when it could not be determined.</summary>
    public string? Variant { get; set; }

    /// <summary>Whether the API extension licence option is available (<c>LicenseOptions.APIExtension</c>); null if not checked.</summary>
    public bool? ApiExtensionLicence { get; set; }
}
