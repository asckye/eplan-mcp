using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>
/// The eplanmcp of the write tests (M3 design 7), with a data root of its own in a temporary folder. Its config.json
/// makes <c>&lt;scratch root&gt;\writes</c> the only scratch folder and refuses every other write, whatever the engineer's
/// own config.json says: the copy outside scratch can only ever be refused, and no approval dialog comes up. The copies
/// come from <c>eval-fixture.ps1 -WriteCopies</c> and are opened in EPLAN by hand (README.md); the copy in
/// <c>writes\</c> is the one the other write tests use too. The tests can run again on them: Q1 and Q2 leave them as
/// they were, Q6 takes new page numbers and texts every time.
/// </summary>
public sealed class WriteHost : IAsyncLifetime
{
    private McpClient? _client;

    /// <summary>This eplanmcp's data root: config.json, audit, state.</summary>
    public string Home { get; } = Directory.CreateTempSubdirectory("eplanmcp-large-").FullName;

    /// <summary>Where the writable copy lies: this eplanmcp's only scratch folder, inside the scratch root.</summary>
    public static string ScratchFolder => Path.Combine(LiveEplan.ScratchRoot(), "writes");

    /// <summary>Where the copy that is only ever refused lies, outside the scratch root.</summary>
    public static string OutsideFolder => Path.Combine(LiveEplan.DataRoot(), "not-scratch", "writes");

    /// <summary>The writable copy as EPLAN reports its path, without an extension.</summary>
    public string Writable { get; private set; } = string.Empty;

    /// <summary>The copy outside the scratch root, as EPLAN reports its path.</summary>
    public string Outside { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var config = new JsonObject { ["scratchRoots"] = new JsonArray(ScratchFolder), ["writesOutsideScratch"] = "deny" };
        File.WriteAllText(Path.Combine(Home, "config.json"), config.ToJsonString());
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "eplanmcp",
            Command = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"),
            Arguments = ["serve"],
            EnvironmentVariables = new Dictionary<string, string?> { ["EPLANMCP_HOME"] = Home },
        });
        _client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);

        string[] open = [.. (await CallAsync("eplan_project_list", new() { ["limit"] = 200 })).GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("path").GetString()!)];
        Writable = OpenedBelow(open, ScratchFolder);
        Outside = OpenedBelow(open, OutsideFolder);

        // The rule the tests rest on: writes go to the scratch root only, and the other copy lies outside it.
        if (!IsUnder(Writable, LiveEplan.ScratchRoot()) || IsUnder(Outside, LiveEplan.ScratchRoot()))
        {
            throw new InvalidOperationException($"{Writable} must lie in the scratch root {LiveEplan.ScratchRoot()} and {Outside} outside it.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        Directory.Delete(Home, recursive: true);
    }

    /// <summary>The structured result of a successful call; a failed call fails the test with the error.</summary>
    public async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?> args)
    {
        CallToolResult result = await _client!.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError != true, $"{tool} failed: {Text(result)}");
        return result.StructuredContent!.Value;
    }

    /// <summary>The <c>error</c> object of a call that must fail.</summary>
    public async Task<JsonElement> ErrorAsync(string tool, Dictionary<string, object?> args)
    {
        CallToolResult result = await _client!.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError, $"{tool} succeeded where it should fail: {Text(result)}");
        return JsonDocument.Parse(Text(result)).RootElement.GetProperty("error");
    }

    /// <summary>Every record of this eplanmcp's audit log, oldest first.</summary>
    public List<JsonObject> AuditRecords()
    {
        string audit = Path.Combine(Home, "audit");
        return Directory.Exists(audit)
            ? [.. Directory.EnumerateFiles(audit, "*.jsonl").Order(StringComparer.Ordinal).SelectMany(File.ReadLines).Select(l => JsonNode.Parse(l)!.AsObject())]
            : [];
    }

    /// <summary>The write suspensions this eplanmcp keeps (ADR-0010 section 3).</summary>
    public string[] Suspensions()
    {
        string state = Path.Combine(Home, "state");
        return Directory.Exists(state) ? [.. Directory.EnumerateFiles(state, "*.json", SearchOption.AllDirectories)] : [];
    }

    /// <summary>The path of the open project in <paramref name="folder"/> or below it; by path, as copies may share the fixture's name.</summary>
    private static string OpenedBelow(string[] open, string folder) =>
        open.FirstOrDefault(p => IsUnder(p, folder))
        ?? throw new InvalidOperationException(
            $"No project below {folder} is open in EPLAN: run eng/scripts/eval-fixture.ps1 -WriteCopies, open the two copies it names in EPLAN, then select the fixture in the page navigator again (README.md).");

    private static bool IsUnder(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
}
