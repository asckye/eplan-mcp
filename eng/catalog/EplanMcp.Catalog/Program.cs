using System.Globalization;

namespace EplanMcp.Catalog;

/// <summary>
/// <c>extract --sdk &lt;EPLAN Bin folder&gt; --out &lt;catalog folder&gt;</c>: writes the catalog of the EPLAN API in that
/// folder. The SDK folder is only read; the path is an argument, so no machine path ends up in the repository.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        string? sdk = Option(args, "--sdk");
        string? output = Option(args, "--out");
        if (args.FirstOrDefault() != "extract" || sdk is null || output is null)
        {
            Console.Error.WriteLine("usage: EplanMcp.Catalog extract --sdk <EPLAN Bin folder> --out <catalog folder> [--namespace Eplan.EplApi]");
            return 2;
        }

        string fullSdk = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sdk));
        string fullOutput = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        if (string.Equals(fullOutput, fullSdk, StringComparison.OrdinalIgnoreCase)
            || fullOutput.StartsWith(fullSdk + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("--out must not be the SDK folder or inside it: the extractor never writes there.");
            return 2;
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

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
