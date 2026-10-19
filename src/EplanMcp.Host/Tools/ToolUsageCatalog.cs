using System.Collections.Frozen;
using System.Reflection;
using System.Text.RegularExpressions;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The texts behind <c>eplan_tool_usage</c>: one embedded Markdown file per tool, <c>Tools/Usage/&lt;tool&gt;.md</c>,
/// made of exactly the four <see cref="Topics"/> as <c>## </c> sections in that order. The texts are written by this
/// project and reviewed with the tool contract (mcp-tool-design 10); they never contain project data.
/// Immutable after the first load, so safe to share between calls.
/// </summary>
internal static partial class ToolUsageCatalog
{
    public const string Examples = "examples";
    public const string Workflow = "workflow";
    public const string Fields = "fields";
    public const string Errors = "errors";

    private const string ResourcePrefix = "EplanMcp.Host.Tools.Usage.";

    private static readonly Lazy<FrozenDictionary<string, IReadOnlyDictionary<string, string>>> _usage = new(Load);

    /// <summary>The values of the <c>topic</c> argument, in the order the sections appear.</summary>
    public static IReadOnlyList<string> Topics { get; } = [Examples, Workflow, Fields, Errors];

    /// <summary>Tool name → topic → that section, heading included. Throws if a file breaks the section rules.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Usage => _usage.Value;

    /// <summary>Splits one usage file into its sections; throws <see cref="InvalidDataException"/> naming the file if it breaks the rules.</summary>
    internal static IReadOnlyDictionary<string, string> Parse(string file, string markdown)
    {
        string[] parts = SectionStart().Split(markdown.ReplaceLineEndings("\n"));
        string[] headings = [.. parts.Skip(1).Select(part => part.Split('\n', 2)[0][3..].Trim())];
        bool valid = parts[0].Trim().Length == 0
            && headings.SequenceEqual(Topics, StringComparer.OrdinalIgnoreCase)
            && parts.Skip(1).All(part => part.Split('\n', 2) is [_, string body] && !string.IsNullOrWhiteSpace(body));
        return valid
            ? Topics.Zip(parts.Skip(1)).ToDictionary(pair => pair.First, pair => pair.Second.TrimEnd(), StringComparer.Ordinal)
            : throw new InvalidDataException(
                $"Tools/Usage/{file} needs the '## ' sections {string.Join(", ", Topics)} in this order, each with text and " +
                $"nothing before the first; found: {(headings.Length == 0 ? "none" : string.Join(", ", headings))}.");
    }

    /// <summary>A malformed file is a build defect: the tests load every file, so the exception never reaches a client.</summary>
    private static FrozenDictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        Assembly assembly = typeof(ToolUsageCatalog).Assembly;
        var usage = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (string resource in assembly.GetManifestResourceNames().Where(r => r.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            string file = resource[ResourcePrefix.Length..];
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            usage[Path.GetFileNameWithoutExtension(file)] = Parse(file, reader.ReadToEnd());
        }

        return usage.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>A line starting with "## " opens a section; "### " does not.</summary>
    [GeneratedRegex("^(?=## )", RegexOptions.Multiline)]
    private static partial Regex SectionStart();
}
