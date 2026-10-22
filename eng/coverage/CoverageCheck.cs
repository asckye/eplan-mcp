using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage;

/// <summary>
/// Whether the committed coverage files match their inputs. Needs no EPLAN. A scan older than the Bridge sources is a
/// warning only: rescanning needs a machine with EPLAN installed, which neither CI nor most agents have, and its source
/// hash still tells that it is out of date.
/// </summary>
internal static class CoverageCheck
{
    public const string ScanCommand = "dotnet run --project eng/coverage -- scan";

    /// <summary>The committed scan, or null when there is none.</summary>
    public static BridgeUsage? Scan(string repositoryRoot) =>
        File.Exists(Path.Combine(repositoryRoot, BridgeUsage.RelativePath)) ? CoverageJson.Read<BridgeUsage>(Path.Combine(repositoryRoot, BridgeUsage.RelativePath)) : null;

    public static bool IsStale(string repositoryRoot, BridgeUsage scan) => scan.SourceHash != BridgeUsage.ComputeSourceHash(repositoryRoot);

    /// <summary>Not failures: a scan the Bridge sources have moved past.</summary>
    public static List<string> Warnings(string repositoryRoot) =>
        Scan(repositoryRoot) is { } scan && IsStale(repositoryRoot, scan)
            ? [$"{BridgeUsage.RelativePath} is out of date: src/EplanMcp.Bridge.Eplan, src/EplanMcp.Bridge.Core or OperationNames changed since the scan of {scan.ScannedOn} ({scan.ScannedSources}). On a machine with EPLAN installed (it need not run): {ScanCommand}"]
            : [];

    /// <summary>Failures: files that are missing or do not match what their inputs give.</summary>
    public static List<string> Problems(string repositoryRoot) =>
        Scan(repositoryRoot) is null ? [$"{BridgeUsage.RelativePath} is missing. On a machine with EPLAN installed: {ScanCommand}"] : [];
}
