using System.Diagnostics;
using System.Globalization;
using EplanMcp.Coverage.Scanning;
using Microsoft.CodeAnalysis;

namespace EplanMcp.Coverage;

/// <summary>
/// <c>scan</c> records the EPLAN API members the Bridge uses (needs EPLAN installed); <c>check</c> tells whether the
/// committed coverage files are current, without EPLAN (a CI step runs it).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? root = RepositoryRoot(Environment.CurrentDirectory);
        if (root is null)
        {
            Console.Error.WriteLine("Run inside the EplanMcp repository (EplanMcp.slnx not found).");
            return 2;
        }

        switch (args)
        {
            case ["scan"]:
                Scan(root, null);
                return 0;
            case ["scan", "--eplan-bin", var eplanBin]:
                Scan(root, eplanBin);
                return 0;
            case ["generate"]:
                CoverageCheck.Generate(root);
                Console.WriteLine("Wrote " + string.Join(", ", CoverageCheck.Generated.Select(g => g.Path)) + ".");
                return 0;
            case ["check"]:
                // In GitHub Actions the lines become annotations on the run and the pull request.
                bool github = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
                List<string> problems = CoverageCheck.Problems(root);
                CoverageCheck.Warnings(root).ForEach(w => Console.WriteLine((github ? "::warning title=Coverage scan::" : "warning: ") + w));
                problems.ForEach(p => Console.WriteLine((github ? "::error title=Coverage files::" : "error: ") + p));
                Console.WriteLine(problems.Count == 0 ? "Coverage files match their inputs." : $"{problems.Count} coverage file(s) out of date.");
                return problems.Count == 0 ? 0 : 1;
            default:
                Console.Error.WriteLine("usage: EplanMcp.Coverage scan [--eplan-bin <Bin folder of the EPLAN installation>] | generate | check");
                return 2;
        }
    }

    /// <summary>The folder above <paramref name="start"/> that holds EplanMcp.slnx.</summary>
    public static string? RepositoryRoot(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static void Scan(string root, string? eplanBin)
    {
        var compilation = BridgeCompilation.Load(root, eplanBin);
        List<OperationRoot> operations = OperationRoot.FromBridgeCore();
        SortedDictionary<string, List<UsageSite>> usages = new UsageScanner(compilation, root).Scan(operations);
        string dataModel = compilation.References.OfType<PortableExecutableReference>()
            .Select(r => r.FilePath!).Single(p => Path.GetFileName(p).Equals("Eplan.EplApi.DataModelu.dll", StringComparison.OrdinalIgnoreCase));
        var usage = new BridgeUsage
        {
            EplanApi = FileVersionInfo.GetVersionInfo(dataModel).FileVersion ?? "unknown",
            ScannedOn = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ScannedSources = BridgeUsage.DescribeSources(root),
            SourceHash = BridgeUsage.ComputeSourceHash(root),
            Operations = [.. operations.Select(o => new OperationEntry(o.Name, ShortName(o.ClassName), o.KindType is { } kind ? $"{ShortName(kind)}.{o.KindValue}" : null))],
            Usages = usages,
        };
        CoverageJson.Write(Path.Combine(root, BridgeUsage.RelativePath), usage);
        CoverageCheck.Generate(root); // the derived files follow the new scan
        int reached = usages.Values.Count(sites => sites.Any(s => s.Operations.Count > 0));
        Console.WriteLine($"{usages.Count} EPLAN API IDs in {usages.Values.Sum(s => s.Count)} use sites, {reached} reached by one of {operations.Count} operations; EPLAN {usage.EplanApi}. Wrote {BridgeUsage.RelativePath}.");
    }

    private static string ShortName(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];
}
