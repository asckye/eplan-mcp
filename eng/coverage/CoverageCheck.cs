using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;
using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage;

/// <summary>
/// The coverage files derived from committed inputs (<c>generate</c> writes them) and whether the committed ones match
/// those inputs. Needs no EPLAN. A scan older than the Bridge sources is a warning only: rescanning needs a machine with
/// EPLAN installed, which neither CI nor most agents have, and its source hash still tells that it is out of date. A
/// derived file that does not match is a failure: anyone can regenerate it.
/// </summary>
internal static class CoverageCheck
{
    public const string ScanCommand = "dotnet run --project eng/coverage -- scan";
    public const string GenerateCommand = "dotnet run --project eng/coverage -- generate";

    /// <summary>Repository-relative path and content of each derived file.</summary>
    public static IEnumerable<(string Path, Func<string, string> Content)> Generated =>
    [
        (ToolMapping.RelativePath, root => CoverageJson.Serialize(ToolMapping.Build(root))),
        (CoverageReport.RelativePath, root => CoverageJson.Serialize(CoverageReport.Build(root))),
        (CoverageMarkdown.RelativePath, CoverageMarkdown.Render),
    ];

    public static void Generate(string repositoryRoot)
    {
        foreach ((string path, Func<string, string> content) in Generated)
        {
            string full = Path.Combine(repositoryRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content(repositoryRoot));
        }
    }

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
    public static List<string> Problems(string repositoryRoot)
    {
        if (Scan(repositoryRoot) is null)
        {
            return [$"{BridgeUsage.RelativePath} is missing. On a machine with EPLAN installed: {ScanCommand}"];
        }

        return [.. Generated
            .Where(g => !File.Exists(Path.Combine(repositoryRoot, g.Path))
                || File.ReadAllText(Path.Combine(repositoryRoot, g.Path)).Replace("\r\n", "\n", StringComparison.Ordinal) != g.Content(repositoryRoot))
            .Select(g => $"{g.Path} does not match its inputs (tools, specs, tests, api-notes, the scan, catalog/mapping/overlay.yaml or the catalog changed). Run, no EPLAN needed: {GenerateCommand}")];
    }
}
