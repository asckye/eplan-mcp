using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;
using EplanMcp.Coverage.Scanning;
using EplanMcp.Coverage.Wrappers;

namespace EplanMcp.Coverage;

/// <summary>
/// The coverage files derived from committed inputs (<c>generate</c> writes them) and whether the committed ones match
/// those inputs. Needs no EPLAN. A scan older than the Bridge sources is a warning only: rescanning needs a machine with
/// EPLAN installed, which neither CI nor most agents have, and its source hash still tells that it is out of date. A
/// derived file that does not match is a failure: anyone can regenerate it. The wrappers catalog/wrappers.yaml selects
/// (<see cref="WrapperGenerator"/>) are derived files too, and a generated file the selection no longer gives is removed.
/// </summary>
internal static class CoverageCheck
{
    public const string ScanCommand = "dotnet run --project eng/coverage -- scan";
    public const string GenerateCommand = "dotnet run --project eng/coverage -- generate";

    /// <summary>Repository-relative path and content of each derived file.</summary>
    public static IEnumerable<(string Path, Func<string, string> Content)> Generated =>
    [
        (PropertyDictionaryGenerator.RelativePath, PropertyDictionaryGenerator.Render),
        (ToolMapping.RelativePath, root => CoverageJson.Serialize(ToolMapping.Build(root))),
        (CoverageReport.RelativePath, root => CoverageJson.Serialize(CoverageReport.Build(root))),
        (CoverageMarkdown.RelativePath, CoverageMarkdown.Render),
        (ApiIndexGenerator.RelativePath, ApiIndexGenerator.Render),
        (ApiTryGenerator.CatalogPath, ApiTryGenerator.Catalog),
        (ApiTryGenerator.DispatchPath, ApiTryGenerator.Dispatch),
        (ApiTryGenerator.ReportPath, ApiTryGenerator.Report),
        (ObjectKindGenerator.TypesPath, ObjectKindGenerator.Types),
        (ObjectKindGenerator.DispatchPath, ObjectKindGenerator.Dispatch),
        (ObjectKindGenerator.TablePath, ObjectKindGenerator.Table),
    ];

    /// <summary>Writes every derived file, wrappers first, removes generated files no longer selected; returns the paths written.</summary>
    public static List<string> Generate(string repositoryRoot)
    {
        SortedDictionary<string, string> wrappers = WrapperGenerator.Files(repositoryRoot);
        Orphans(repositoryRoot, wrappers).ForEach(path => File.Delete(Path.Combine(repositoryRoot, path)));
        foreach ((string path, string content) in wrappers)
        {
            Write(repositoryRoot, path, content);
        }

        foreach ((string path, Func<string, string> content) in Generated)
        {
            Write(repositoryRoot, path, content(repositoryRoot));
        }

        return [.. wrappers.Keys, .. Generated.Select(g => g.Path)];
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
        SortedDictionary<string, string> wrappers = WrapperGenerator.Files(repositoryRoot);
        string[] wrapperProblems =
        [
            .. wrappers.Where(w => !Matches(repositoryRoot, w.Key, w.Value))
                .Select(w => $"{w.Key} does not match {WrapperSelection.RelativePath} and the catalog. Run, no EPLAN needed: {GenerateCommand}"),
            .. Orphans(repositoryRoot, wrappers).Select(path => $"{path} is no longer selected by {WrapperSelection.RelativePath}. Run, no EPLAN needed, to remove it: {GenerateCommand}"),
        ];
        if (Scan(repositoryRoot) is null)
        {
            return [.. wrapperProblems, $"{BridgeUsage.RelativePath} is missing. On a machine with EPLAN installed: {ScanCommand}"];
        }

        return [.. wrapperProblems, .. Generated
            .Where(g => !Matches(repositoryRoot, g.Path, g.Content(repositoryRoot)))
            .Select(g => $"{g.Path} does not match its inputs (tools, specs, tests, api-notes, the scan, catalog/mapping/overlay.yaml or the catalog changed). Run, no EPLAN needed: {GenerateCommand}")];
    }

    /// <summary>Whether the committed file is <paramref name="content"/>, line endings aside.</summary>
    private static bool Matches(string repositoryRoot, string path, string content) =>
        File.Exists(Path.Combine(repositoryRoot, path))
        && File.ReadAllText(Path.Combine(repositoryRoot, path)).Replace("\r\n", "\n", StringComparison.Ordinal) == content;

    /// <summary>Generated files (<c>*.g.cs</c>) in the generator's folders that <paramref name="wrappers"/> no longer has.</summary>
    private static List<string> Orphans(string repositoryRoot, SortedDictionary<string, string> wrappers) =>
    [
        .. new[] { WrapperGenerator.ContractsFolder, WrapperGenerator.BridgeFolder }
            .Select(folder => Path.Combine(repositoryRoot, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.g.cs"))
            .Select(file => Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'))
            .Where(path => !wrappers.ContainsKey(path) && !Generated.Any(g => g.Path == path))
            .Order(StringComparer.Ordinal),
    ];

    private static void Write(string repositoryRoot, string path, string content)
    {
        string full = Path.Combine(repositoryRoot, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
