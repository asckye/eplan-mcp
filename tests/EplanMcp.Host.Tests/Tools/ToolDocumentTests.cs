using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed partial class ToolDocumentTests(RegisteredToolFixture fixture)
{
    private static readonly string _docs = Path.Combine(RepositoryRoot(), "docs");
    private static readonly string _toolDocs = Path.Combine(_docs, "tools");

    [Fact]
    public void RegisteredToolsAndSpecificationFilesMatch()
    {
        var registered = fixture.Registered.Select(t => t.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal);
        var expected = registered.Select(name => name + ".md");
        var files = Directory.EnumerateFiles(_toolDocs, "eplan_*.md").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        // Only generated .g.md files are exempt from the reverse check: they are reference data, not specifications.
        var specifications = files.Where(name => !name!.EndsWith(".g.md", StringComparison.Ordinal));
        AssertNoIssues(expected.Except(files).Select(name => $"Missing specification: {name}")
            .Concat(specifications.Except(expected).Select(name => $"Unregistered specification: {name}")));
    }

    [Fact]
    public void SpecificationsNameTheTopLevelInputParameters()
    {
        var issues = new List<string>();
        foreach (Tool tool in fixture.Registered.Select(t => t.ProtocolTool))
        {
            string file = Path.Combine(_toolDocs, tool.Name + ".md");
            if (!File.Exists(file))
            {
                issues.Add($"{tool.Name}: missing specification");
                continue;
            }

            string document = File.ReadAllText(file);
            var parameters = tool.InputSchema.GetProperty("properties").EnumerateObject()
                .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            var named = InlineCode().Matches(document).Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
            issues.AddRange(parameters.Except(named).Select(name => $"{tool.Name}: undocumented parameter {name}"));

            foreach (string[] row in TableRows(document, "参数"))
            {
                // A qualified first name also qualifies shorthand siblings, e.g. items[].potential_name / signal_name.
                foreach (string field in row[0].Split(['、', '/'], StringSplitOptions.TrimEntries))
                {
                    string name = field.Trim('`');
                    if (name.Contains("[].", StringComparison.Ordinal))
                    {
                        break;
                    }
                    if (!Identifier().IsMatch(name) || parameters.Contains(name))
                    {
                        continue;
                    }
                    // Unquoted tables also describe item properties and kind discriminators in this column.
                    if (!field.StartsWith('`') && ContainsItemField(tool.InputSchema, name))
                    {
                        continue;
                    }
                    issues.Add($"{tool.Name}: unknown parameter in table: {name}");
                }
            }
        }
        AssertNoIssues(issues);
    }

    private static bool ContainsItemField(JsonElement schema, string name)
    {
        if (schema.ValueKind == JsonValueKind.Array)
        {
            return schema.EnumerateArray().Any(item => ContainsItemField(item, name));
        }
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        if (schema.TryGetProperty("properties", out JsonElement properties) && properties.TryGetProperty(name, out _))
        {
            return true;
        }
        return (schema.TryGetProperty("const", out JsonElement value) && value.ValueKind == JsonValueKind.String
                && value.GetString() == name)
            || schema.EnumerateObject().Any(property => ContainsItemField(property.Value, name));
    }

    [Fact]
    public void IndexListsEachToolOnceWithItsRegisteredProfiles()
    {
        var issues = new List<string>();
        var profiles = fixture.Registered.ToDictionary(t => t.ProtocolTool.Name, _ => new HashSet<string>(StringComparer.Ordinal));
        foreach (ToolProfile profile in Enum.GetValues<ToolProfile>())
        {
            List<McpServerTool> offered = [.. fixture.Registered];
            ToolProfiles.Apply(offered, profile);
            foreach (McpServerTool tool in offered)
            {
                profiles[tool.ProtocolTool.Name].Add(profile.ToString().ToLowerInvariant());
            }
        }

        string index = File.ReadAllText(Path.Combine(_toolDocs, "README.md"));
        var rows = TableRows(index, "工具", "Profile").ToLookup(row => row[0].Trim('`'), StringComparer.Ordinal);
        foreach (string name in profiles.Keys.Except(rows.Select(group => group.Key)))
        {
            issues.Add($"{name}: missing from index");
        }
        foreach (var group in rows)
        {
            if (group.Count() > 1)
            {
                issues.Add($"{group.Key}: indexed {group.Count()} times");
            }
            if (!profiles.TryGetValue(group.Key, out HashSet<string>? expected))
            {
                issues.Add($"{group.Key}: unregistered tool in index");
                continue;
            }
            foreach (string[] row in group)
            {
                string stated = row.Length > 1 ? row[1] : "";
                if (!expected.SetEquals(stated.Split('、', StringSplitOptions.TrimEntries)))
                {
                    issues.Add($"{group.Key}: expected {string.Join(", ", expected.Order(StringComparer.Ordinal))}, found '{stated}'");
                }
            }
        }
        AssertNoIssues(issues);
    }

    private static IEnumerable<string[]> TableRows(string document, params string[] header)
    {
        bool inTable = false;
        foreach (string line in document.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith('|'))
            {
                inTable = false;
                continue;
            }
            string[] cells = trimmed.Trim('|').Split('|', StringSplitOptions.TrimEntries);
            if (cells.Take(header.Length).SequenceEqual(header))
            {
                inTable = true;
            }
            else if (inTable && !cells[0].Trim(':').All(c => c == '-'))
            {
                yield return cells;
            }
        }
    }

    [Fact]
    public void RevisedAdrsLinkForwardAndSupersededStatusesNameTheirSuccessors()
    {
        var issues = new List<string>();
        var decisions = Directory.EnumerateFiles(Path.Combine(_docs, "adr"), "????-*.md")
            .Where(path => Path.GetFileName(path).Take(4).All(char.IsAsciiDigit))
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllText, StringComparer.Ordinal);
        var links = decisions.ToDictionary(entry => entry.Key,
            entry => AdrLink().Matches(entry.Value).Select(match => match.Groups["file"].Value).ToHashSet(StringComparer.Ordinal));

        foreach ((string file, string document) in decisions)
        {
            foreach (string earlier in RevisedDecisions(document).Distinct(StringComparer.Ordinal))
            {
                if (earlier.AsSpan(0, 4).SequenceCompareTo(file.AsSpan(0, 4)) >= 0)
                {
                    continue;
                }
                if (!links.TryGetValue(earlier, out var outgoing))
                {
                    issues.Add($"{file}: revision target {earlier} does not exist");
                }
                else if (!outgoing.Contains(file))
                {
                    issues.Add($"{earlier}: missing forward link to {file}");
                }
            }

            foreach (Match status in SupersededStatus().Matches(document))
            {
                Match successor = SuccessorLink().Match(status.Groups[1].Value);
                string target = successor.Groups["file"].Value;
                if (!decisions.ContainsKey(target) || string.CompareOrdinal(target, file) <= 0
                    || !successor.Groups["label"].Value.Contains(target[..4], StringComparison.Ordinal))
                {
                    issues.Add($"{file}: superseded status needs a link naming its successor");
                }
            }
        }
        AssertNoIssues(issues);
    }

    private static IEnumerable<string> RevisedDecisions(string document)
    {
        // Clauses in one impact cell can revise one decision and keep or merely reference another.
        foreach (string clause in ClauseBoundary().Split(document))
        {
            int previousEnd = 0;
            bool revised = false;
            foreach (Match link in AdrLink().Matches(clause))
            {
                string prefix = clause[previousEnd..link.Index];
                string suffix = clause[(link.Index + link.Length)..];
                revised = RevisionVerb().IsMatch(prefix)
                    || (revised && Conjunction().IsMatch(prefix))
                    || (ObjectMarker().IsMatch(prefix) && RevisionOfObject().IsMatch(suffix))
                    || PassiveRevision().IsMatch(suffix);
                if (revised)
                {
                    yield return link.Groups["file"].Value;
                }
                previousEnd = link.Index + link.Length;
            }
        }
    }

    private const string AdrPattern = @"\[(?<label>[^\]\r\n]+)\]\((?:\./|\.\./adr/)?(?<file>\d{4}-[\w-]+\.md)(?:#[^)]*)?\)";

    [GeneratedRegex(@"(?<!`)`([^`\r\n]+)`(?!`)")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z_0-9]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(AdrPattern)]
    private static partial Regex AdrLink();

    [GeneratedRegex(@"[|。；;!?！？]|\.(?=\s|$)|\r?\n\s*\r?\n")]
    private static partial Regex ClauseBoundary();

    [GeneratedRegex(@"(?:(?<!不)(?:修订|取代|替代|替换|补充|修改|扩展|放宽|改变|细化)了?|(?<!not )\b(?:revis(?:es?|ed)|supersed(?:es?|ed)|replac(?:es?|ed)|amend(?:s|ed)?)(?:\s+(?:part|section \d+) of)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RevisionVerb();

    [GeneratedRegex(@"(?:、|和|与|及|,|\band)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Conjunction();

    [GeneratedRegex(@"(?:给|对|将|把)\s*$")]
    private static partial Regex ObjectMarker();

    [GeneratedRegex(@"^[^\[，,]*(?:加一条例外|作出修订|进行修订|进行修改)")]
    private static partial Regex RevisionOfObject();

    [GeneratedRegex(@"^\s*(?:的[^\[，,（）]*要补充|被本\s*ADR\s*(?:修订|取代)|(?:is|was) (?:superseded|revised|replaced|amended) by (?:this|the present) (?:decision|ADR))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PassiveRevision();

    [GeneratedRegex(@"^\s*\|\s*状态\s*\|\s*([^|\r\n]*(?:\bsuperseded\b|取代|替代)[^|\r\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SupersededStatus();

    [GeneratedRegex(@"(?:\bsuperseded\s+by\s+" + AdrPattern + @"|(?:被|由)\s*" + AdrPattern + @"\s*(?:取代|替代))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuccessorLink();

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("EplanMcp.slnx not found above " + AppContext.BaseDirectory);
    }

    private static void AssertNoIssues(IEnumerable<string> issues)
    {
        string[] all = [.. issues.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Assert.True(all.Length == 0, string.Join(Environment.NewLine, all));
    }
}
