using System.Globalization;

namespace EplanMcp.Catalog;

/// <summary>
/// <c>extract --sdk &lt;EPLAN Bin folder&gt; --out &lt;catalog folder&gt;</c>: writes the catalog of the EPLAN API in that
/// folder. The SDK folder is only read; the path is an argument, so no machine path ends up in the repository.
/// <c>diff --base &lt;catalog&gt; --other &lt;catalog&gt; --out &lt;tsv&gt; [--usage &lt;bridge-usage.json&gt;]</c>: the IDs in
/// only one of two catalogs (<see cref="CatalogDiff"/>).
/// </summary>
internal static class Program
{
    private const string Usage = "usage: EplanMcp.Catalog extract --sdk <EPLAN Bin folder> --out <catalog folder> [--namespace Eplan.EplApi]"
        + " | diff --base <catalog folder> --other <catalog folder> --out <tsv file> [--usage <bridge-usage.json>]";

    public static int Main(string[] args) => args.FirstOrDefault() switch
    {
        "extract" => Extract(args),
        "diff" => Diff(args),
        _ => Fail(Usage),
    };

    private static int Extract(string[] args)
    {
        string? sdk = Option(args, "--sdk");
        string? output = Option(args, "--out");
        if (sdk is null || output is null)
        {
            return Fail(Usage);
        }

        string fullSdk = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sdk));
        string fullOutput = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        if (string.Equals(fullOutput, fullSdk, StringComparison.OrdinalIgnoreCase)
            || fullOutput.StartsWith(fullSdk + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("--out must not be the SDK folder or inside it: the extractor never writes there.");
        }

        CatalogData catalog = Extractor.Extract(fullSdk, Option(args, "--namespace") ?? "Eplan.EplApi");
        Directory.CreateDirectory(fullOutput);
        CatalogWriter.Write(catalog, fullOutput);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{catalog.Entries.Count} entries from {catalog.Assemblies.Count(a => a.Skipped is null)} assemblies; {catalog.Xml.Unmatched.Count} IntelliSense IDs unmatched."));
        foreach (string id in catalog.Xml.Unmatched.Take(50))
        {
            Console.Error.WriteLine("unmatched: " + id);
        }

        return 0;
    }

    private static int Diff(string[] args)
    {
        string? baseFolder = Option(args, "--base");
        string? otherFolder = Option(args, "--other");
        string? output = Option(args, "--out");
        if (baseFolder is null || otherFolder is null || output is null)
        {
            return Fail(Usage);
        }

        string? usage = Option(args, "--usage");
        IReadOnlySet<string> used = usage is null ? new HashSet<string>(StringComparer.Ordinal) : CatalogDiff.Usages(usage);
        IReadOnlyList<CatalogDifference> differences = CatalogDiff.Compare(baseFolder, otherFolder, used);
        CatalogDiff.Write(differences, output);
        Console.WriteLine(CatalogDiff.Summary(differences));
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
