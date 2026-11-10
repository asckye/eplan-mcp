using System.Text.RegularExpressions;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

/// <summary>
/// The add-in takes an EPLAN wrapper's <c>Properties</c> once per method and reuses the list: taking it repeatedly from
/// the same wrapper is the pattern behind the 0xc0000374 heap-corruption crashes in api-notes. This scans the add-in
/// source; the exceptions read managed snapshots whose <c>Properties</c> is a plain dictionary, not a native list.
/// </summary>
[Trait("Size", "Small")]
public sealed partial class PropertyListOnceTests
{
    private static readonly HashSet<string> _managedReceivers = new(StringComparer.Ordinal)
    {
        "EplanPageReviewReader.cs:p",       // DrawingPlacement snapshot (managed dictionary)
        "EplanPlaceholderReader.cs:record", // PlaceHolderApi record (managed)
    };

    [Fact]
    public void AddIn_TakesEachWrappersPropertiesOncePerMethod()
    {
        string root = Path.Combine(RepositoryRoot(), "src", "EplanMcp.Bridge.Eplan");
        var repeats = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name.EndsWith(".g.cs", StringComparison.Ordinal) || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            { continue; }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (MethodStart().IsMatch(lines[i]))
                { seen.Clear(); }
                foreach (Match use in PropertiesUse().Matches(lines[i]))
                {
                    string receiver = use.Groups[1].Value;
                    if (_managedReceivers.Contains(name + ":" + receiver))
                    { continue; }
                    if (seen.TryGetValue(receiver, out int first) && first != i)
                    { repeats.Add($"{name}:{i + 1} '{receiver}.Properties' again (first at line {first + 1}); take it once into a local"); }
                    seen.TryAdd(receiver, i);
                }
            }
        }

        Assert.Empty(repeats);
    }

    [GeneratedRegex(@"^    (?:public|private|internal|protected)[^;=]*\(")]
    private static partial Regex MethodStart();

    [GeneratedRegex(@"\b([A-Za-z_][A-Za-z0-9_]*)\.Properties\.[A-Za-z_]")]
    private static partial Regex PropertiesUse();

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            { return directory.FullName; }
        }

        throw new InvalidOperationException("EplanMcp.slnx not found above " + AppContext.BaseDirectory);
    }
}
