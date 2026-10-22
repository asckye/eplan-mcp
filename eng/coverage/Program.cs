using System.Diagnostics;
using EplanMcp.Coverage.Scanning;
using Microsoft.CodeAnalysis;

namespace EplanMcp.Coverage;

/// <summary><c>scan</c> records the EPLAN API members the Bridge uses; it needs EPLAN installed.</summary>
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
            default:
                Console.Error.WriteLine("usage: EplanMcp.Coverage scan [--eplan-bin <Bin folder of the EPLAN installation>]");
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
            Operations = [.. operations.Select(o => new OperationEntry(o.Name, ShortName(o.ClassName), o.KindType is { } kind ? $"{ShortName(kind)}.{o.KindValue}" : null))],
            Usages = usages,
        };
        CoverageJson.Write(Path.Combine(root, BridgeUsage.RelativePath), usage);
        int reached = usages.Values.Count(sites => sites.Any(s => s.Operations.Count > 0));
        Console.WriteLine($"{usages.Count} EPLAN API IDs in {usages.Values.Sum(s => s.Count)} use sites, {reached} reached by one of {operations.Count} operations; EPLAN {usage.EplanApi}. Wrote {BridgeUsage.RelativePath}.");
    }

    private static string ShortName(string fullName) => fullName[(fullName.LastIndexOf('.') + 1)..];
}
