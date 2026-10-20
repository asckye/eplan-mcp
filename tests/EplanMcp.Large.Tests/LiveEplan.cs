using System.Text.Json;
using EplanMcp.Large.Tests;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit.Sdk;
using Xunit.v3;

// One eplanmcp for the whole run, and one test at a time: every test talks to the same EPLAN, whose main thread runs
// the calls one by one anyway, and the side-effect test must see no other calls.
[assembly: AssemblyFixture(typeof(LiveEplan))]
[assembly: Parallelization(Mode = ParallelMode.None)]

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplanmcp connected to the running EPLAN, with the fixture project current (README.md). Starting fails with what to
/// do when EPLAN, the add-in or the fixture is missing, so every test reports it instead of a confusing assertion.
/// </summary>
public sealed class LiveEplan : IAsyncLifetime
{
    /// <summary>The fixture: EPLAN's sample project, copied into the scratch root by eng/scripts/eval-fixture.ps1.</summary>
    public const string ProjectName = "EPLAN_Sample_Project";

    private McpClient? _client;

    /// <summary>The fixture's project path as EPLAN reports it, without an extension.</summary>
    public string ProjectPath { get; private set; } = string.Empty;

    /// <summary>The process id of the EPLAN the tests talk to.</summary>
    public int ProcessId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "eplanmcp",
            Command = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"),
            Arguments = ["serve"],
        });
        _client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);

        JsonElement status = await CallAsync("eplan_session_status");
        if (!status.GetProperty("connected").GetBoolean())
        {
            throw new InvalidOperationException(
                "eplanmcp does not reach EPLAN: start it with the fixture (eng/scripts/eval-fixture.ps1 -Open) and register the add-in. Hint: "
                + status.GetProperty("hint").GetString());
        }

        ProcessId = status.GetProperty("eplan").GetProperty("processId").GetInt32();
        JsonElement current = (await CallAsync("eplan_project_list")).GetProperty("projects").EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("current").GetBoolean());
        string path = current.ValueKind == JsonValueKind.Object ? current.GetProperty("path").GetString()! : string.Empty;
        string fixtures = Path.Combine(ScratchRoot(), "fixtures") + Path.DirectorySeparatorChar;
        if (!path.StartsWith(fixtures, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFileName(path), ProjectName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The current EPLAN project is not the fixture {fixtures}{ProjectName}: open it with eng/scripts/eval-fixture.ps1 -Open. Current: '{path}'.");
        }

        ProjectPath = path;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    /// <summary>The structured result of a successful call; a failed call fails the test with the error.</summary>
    public async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?>? args = null)
    {
        CallToolResult result = await CallRawAsync(tool, args);
        Assert.True(result.IsError != true, $"{tool} failed: {Text(result)}");
        return result.StructuredContent!.Value;
    }

    /// <summary>The <c>error</c> object of a call that must fail.</summary>
    public async Task<JsonElement> ErrorAsync(string tool, Dictionary<string, object?> args)
    {
        CallToolResult result = await CallRawAsync(tool, args);
        Assert.True(result.IsError, $"{tool} succeeded where it should fail: {Text(result)}");
        return JsonDocument.Parse(Text(result)).RootElement.GetProperty("error");
    }

    /// <summary>The structured result, or the <c>error</c> object, of a call that may succeed or fail.</summary>
    public async Task<(bool Succeeded, JsonElement Result)> CallOrErrorAsync(string tool, Dictionary<string, object?> args)
    {
        CallToolResult result = await CallRawAsync(tool, args);
        return result.IsError == true
            ? (false, JsonDocument.Parse(Text(result)).RootElement.GetProperty("error"))
            : (true, result.StructuredContent!.Value);
    }

    /// <summary>
    /// The path of the project write tests change: a copy of the sample project below the scratch root's <c>writes</c>
    /// folder, open next to the fixture (README.md), so the fixture the read tests count stays as it is. The path, not
    /// the name, because both copies are called <see cref="ProjectName"/>.
    /// </summary>
    public async Task<string> WriteProjectAsync()
    {
        string writes = Path.Combine(ScratchRoot(), "writes") + Path.DirectorySeparatorChar;
        JsonElement open = (await CallAsync("eplan_project_list")).GetProperty("projects").EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("path").GetString()!.StartsWith(writes, StringComparison.OrdinalIgnoreCase));
        return open.ValueKind == JsonValueKind.Object
            ? open.GetProperty("path").GetString()!
            : throw new InvalidOperationException($"No project below {writes} is open in EPLAN: copy the sample project there and open it next to the fixture (README.md).");
    }

    private Task<CallToolResult> CallRawAsync(string tool, Dictionary<string, object?>? args) =>
        _client!.CallToolAsync(tool, args ?? [], cancellationToken: TestContext.Current.CancellationToken).AsTask();

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    /// <summary>The data root eplanmcp uses (ADR-0008): <c>EPLANMCP_HOME</c>, or <c>%USERPROFILE%\EplanMcp</c>.</summary>
    internal static string DataRoot() =>
        Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } home ? home : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");

    /// <summary>The scratch root eplanmcp uses by default: <c>scratch</c> under <see cref="DataRoot"/>.</summary>
    internal static string ScratchRoot() => Path.Combine(DataRoot(), "scratch");
}
