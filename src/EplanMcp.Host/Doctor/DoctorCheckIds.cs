namespace EplanMcp.Host.Doctor;

/// <summary>
/// The stable IDs of the doctor checks, by which the Workbench groups them (M5 design 4.4). Part of the doctor JSON:
/// every ID is listed in architecture 5.2, and a new one belongs there too.
/// </summary>
internal static class DoctorCheckIds
{
    public const string HostVersion = "host.version";
    public const string HttpService = "http.service";
    public const string VmFirewall = "vm.firewall";
    public const string DataDirectory = "data.directory";
    public const string Config = "config";
    public const string AuditDirectory = "audit.directory";
    public const string Exports = "exports";
    public const string Backups = "backups";
    public const string Msix = "msix";
    public const string Suspension = "suspension";
    public const string EplanInstallation = "eplan.installation";
    public const string EplanRunning = "eplan.running";
    public const string BridgePipe = "bridge.pipe";
    public const string BridgeHandshake = "bridge.handshake";
    public const string BridgeLicence = "bridge.licence";
    public const string PathLength = "path.length";
}
