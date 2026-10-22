using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage.Mapping;

/// <summary>
/// What a tool's evidence is: its spec and the spec's status row, the api-notes sections the spec cites, the test classes
/// that name the tool, and the test classes that name its operations' classes (Bridge.Core tests with fake adapters).
/// </summary>
internal sealed record ToolEvidence(string? Spec, string? SpecStatus, IReadOnlyList<string> ApiNotes, IReadOnlyList<TestClass> Tests, IReadOnlyList<TestClass> OperationTests);

/// <summary>One MCP tool: safety level from its annotations, Host file, Bridge operations, EPLAN API IDs, actions, evidence.</summary>
internal sealed record ToolEntry(string Name, string Safety, string Host, IReadOnlyList<string> Operations, IReadOnlyList<string> Api, IReadOnlyList<string> Actions, ToolEvidence Evidence);

/// <summary>
/// catalog/mapping/tools.json: every tool of the contract snapshot mapped through the Host's tool code to Bridge
/// operations and, through bridge-usage.json, to EPLAN API IDs; <see cref="Infrastructure"/> are the IDs no operation
/// reaches (add-in start, dispatcher). Derived only from files in the repository, so CI regenerates and compares it.
/// </summary>
internal sealed partial class ToolMapping
{
    public const string RelativePath = "catalog/mapping/tools.json";
    private const string ContractSnapshot = "tests/Contracts.Snapshots/server-contract.verified.json";

    public required string EplanApi { get; init; }

    public required IReadOnlyList<ToolEntry> Tools { get; init; }

    public required IReadOnlyList<string> Infrastructure { get; init; }

    public static ToolMapping Build(string repositoryRoot)
    {
        BridgeUsage usage = CoverageJson.Read<BridgeUsage>(Path.Combine(repositoryRoot, BridgeUsage.RelativePath));
        (List<HostTool> hostTools, Dictionary<string, string> references) = HostTools.Read(repositoryRoot);
        var tests = new TestIndex(repositoryRoot);
        HashSet<string> sections = [.. Heading().Matches(File.ReadAllText(Path.Combine(repositoryRoot, "docs", "eplan", "api-notes.md"))).Select(m => m.Groups[1].Value)];
        ILookup<string, string> idsByOperation = usage.Usages
            .SelectMany(u => u.Value.SelectMany(s => s.Operations).Distinct().Select(operation => (Operation: operation, Id: u.Key)))
            .ToLookup(p => p.Operation, p => p.Id);
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, ContractSnapshot)));
        var tools = new List<ToolEntry>();
        foreach (JsonElement tool in contract.RootElement.GetProperty("tools").EnumerateArray())
        {
            string name = tool.GetProperty("name").GetString()!;
            HostTool host = hostTools.SingleOrDefault(h => h.Name == name)
                ?? throw new InvalidDataException($"{name} is in {ContractSnapshot} but no [McpServerTool] method in src/EplanMcp.Host/Tools has that name.");
            string[] ids = [.. host.Operations.SelectMany(o => idsByOperation[o]).Distinct().Order(StringComparer.Ordinal)];
            string[] classes = [.. usage.Operations.Where(o => host.Operations.Contains(o.Name)).Select(o => o.Class).Distinct()];
            string[] names = [name, .. references.Where(r => r.Value == name).Select(r => r.Key)];
            tools.Add(new ToolEntry(
                name,
                Safety(tool.GetProperty("annotations")),
                host.File,
                host.Operations,
                [.. ids.Where(id => !id.StartsWith("A:", StringComparison.Ordinal))],
                [.. ids.Where(id => id.StartsWith("A:", StringComparison.Ordinal))],
                Evidence(repositoryRoot, name, sections, tests.Using(names), tests.Using(classes))));
        }

        return new ToolMapping
        {
            EplanApi = usage.EplanApi,
            Tools = [.. tools.OrderBy(t => t.Name, StringComparer.Ordinal)],
            Infrastructure = [.. usage.Usages.Where(u => u.Value.All(s => s.Operations.Count == 0)).Select(u => u.Key)],
        };
    }

    private static string Safety(JsonElement annotations) =>
        Flag(annotations, "readOnlyHint") ? "read" : Flag(annotations, "destructiveHint") ? "destructive" : "write";

    private static bool Flag(JsonElement annotations, string name) => annotations.TryGetProperty(name, out JsonElement value) && value.GetBoolean();

    /// <summary>
    /// The spec's status row and the api-notes sections it cites: an A/B number in prose next to brackets or list
    /// punctuation, outside code, that is a heading of api-notes (so device tags such as +A1 and pins "A1" do not count).
    /// </summary>
    private static ToolEvidence Evidence(string root, string tool, HashSet<string> sections, List<TestClass> tests, List<TestClass> operationTests)
    {
        string spec = $"docs/tools/{tool}.md";
        if (!File.Exists(Path.Combine(root, spec)))
        {
            return new ToolEvidence(null, null, [], tests, operationTests);
        }

        string text = File.ReadAllText(Path.Combine(root, spec));
        string prose = Code().Replace(text, " ");
        string[] cited = [.. Citation().Matches(prose).Select(m => m.Value).Where(sections.Contains).Distinct()
            .OrderBy(s => s[0]).ThenBy(s => int.Parse(s[1..], System.Globalization.CultureInfo.InvariantCulture))];
        return new ToolEvidence(spec, Status().Match(text) is { Success: true } status ? status.Groups[1].Value.Trim() : null, cited, tests, operationTests);
    }

    [GeneratedRegex(@"^### ([AB]\d+)\.", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex("```.*?```|`[^`\n]*`", RegexOptions.Singleline)]
    private static partial Regex Code();

    [GeneratedRegex(@"(?<=[（(，、,\s])[AB]\d{1,2}(?=[）)，、,：:\s])")]
    private static partial Regex Citation();

    [GeneratedRegex(@"^\| 状态 \| (.+?) \|\s*$", RegexOptions.Multiline)]
    private static partial Regex Status();
}
