using System.Text.RegularExpressions;

namespace EplanMcp.Coverage;

/// <summary>Evidence headings from archived topics and entries still appended to the index.</summary>
internal static partial class ApiNotes
{
    internal const string IndexPath = "docs/eplan/api-notes.md";

    internal static Dictionary<string, string> Anchors(string root)
    {
        string directory = Path.Combine(root, "docs/eplan/api-notes");
        IEnumerable<string> paths = new[] { IndexPath }.Concat(Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.md").Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).Order(StringComparer.Ordinal)
            : []);
        return paths.SelectMany(path => File.ReadLines(Path.Combine(root, path))
            .Where(line => Heading().IsMatch(line)).Select(line => (Path: path, Line: line)))
            .ToDictionary(entry => Heading().Match(entry.Line).Groups[1].Value,
                entry => entry.Path + "#" + Regex.Replace(entry.Line[4..].ToLowerInvariant(), @"[^\p{L}\p{N}_ -]", "").Replace(' ', '-'),
                StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^### ([AB]\d+)\.")]
    private static partial Regex Heading();
}
