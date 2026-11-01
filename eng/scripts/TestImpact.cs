#if !NET
// PowerShell Add-Type does not provide the test project's implicit usings.
using System;
using System.IO;
using System.Linq;
#endif
using System.Text.RegularExpressions;
using System.Xml;

namespace EplanMcp.Build;

/// <summary>Pure path mapping shared by affected-tests.ps1 and its Small tests.</summary>
public static class TestImpact
{
    /// <summary>Includes both source and destination paths from git diff --name-status -M.</summary>
    public static string[] ChangedPaths(string[] records) => records
        .SelectMany(record => record.Split('\t').Skip(1)).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Matches physical external item paths; Link is a virtual name, never the source path.</summary>
    public static bool LinksFile(string projectFile, string projectXml, string changedFile)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;
        string prefix = Normalize(directory) + "/";
        string changed = Normalize(Path.GetFullPath(changedFile));
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(projectXml);
        foreach (XmlElement item in document.SelectNodes("//*[local-name()='Compile' or local-name()='EmbeddedResource' or local-name()='None' or local-name()='Content']")!)
        {
            foreach (string include in Patterns(item.GetAttribute("Include")))
            {
                string pattern = Resolve(directory, include);
                if (!pattern.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Matches(changed, pattern)
                    && !Patterns(item.GetAttribute("Exclude")).Any(exclude => Matches(changed, Resolve(directory, exclude))))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] Patterns(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Resolve(string directory, string pattern) => Normalize(Path.GetFullPath(Path.Combine(directory,
        pattern.Replace("$(MSBuildThisFileDirectory)", directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            .Replace('\\', Path.DirectorySeparatorChar))));

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static bool Matches(string file, string pattern)
    {
        string expression = Regex.Escape(pattern).Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", "[^/]", StringComparison.Ordinal);
        return Regex.IsMatch(file, "^" + expression + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
