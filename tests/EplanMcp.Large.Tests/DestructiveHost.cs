using System.Globalization;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>
/// The Host of the Destructive tools' Large tests (M4 design 7; tool spec eplan_page_delete, section 7), composed in this
/// test process as serve composes it and talking to the real Bridge, but with <see cref="ScriptedApprovalChannel"/> as
/// the engineer: eplanmcp.exe can never approve a write by itself, and EPLAN's dialog needs a person. Like
/// <see cref="WriteHost"/> it has a data root of its own in a temporary folder, which makes
/// <c>&lt;scratch root&gt;\writes</c> the only scratch folder and refuses writes to any other project; the backups the
/// Destructive writes make of the copy lie in that data root too and go with it. The tests write only new pages of
/// <see cref="WriteHost.WritableName"/> after the last one of <see cref="Prefix"/> and delete them again (README.md).
/// </summary>
public sealed class DestructiveHost : IAsyncLifetime
{
    /// <summary>The structure the tests add their pages to, as the other write tests do.</summary>
    public const string Prefix = "=GAA+A1&EFS1/";

    private IHost? _host;
    private McpClient? _client;
    private string[] _open = [];

    /// <summary>This Host's data root: audit, state, backups.</summary>
    public string Home { get; } = Directory.CreateTempSubdirectory("eplanmcp-large-").FullName;

    /// <summary>The engineer, as far as these tests ask one.</summary>
    internal ScriptedApprovalChannel Approvals { get; } = new();

    /// <summary>The writable copy as EPLAN reports its path, without an extension.</summary>
    public string Writable { get; private set; } = string.Empty;

    /// <summary>The EPLAN this Host talks to.</summary>
    public int ProcessId { get; private set; }

    /// <summary>False for tests that write no project, such as the parts database's (PartImportTests): no copy need be open.</summary>
    public bool RequireWritable { get; init; } = true;

    /// <summary>The copy outside the scratch root, which is only ever refused; fails with what to do when it is not open.</summary>
    public string Outside => WriteHost.OpenedBelow(_open, WriteHost.OutsideFolder, null);

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var toServer = new Pipe();
        var toClient = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new HostDataLocations(Home));
        builder.Services.AddSingleton(new HostConfig([WriteHost.ScratchFolder], OutsideScratchWrites.Deny));
        builder.Services.AddEplanMcp().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.RemoveAll<IApprovalChannel>();
        builder.Services.AddSingleton<IApprovalChannel>(Approvals);
        _host = builder.Build();
        await _host.StartAsync(cancellationToken);
        _client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: cancellationToken);

        // Like WriteHost's eplanmcp, this Host talks to the one EPLAN there is; with several it would refuse every call.
        JsonElement status = await CallAsync("eplan_session_status", []);
        ProcessId = status.GetProperty("connected").GetBoolean()
            ? status.GetProperty("eplan").GetProperty("processId").GetInt32()
            : throw new InvalidOperationException("The Destructive tests' Host does not reach EPLAN: " + status.GetProperty("hint").GetString());
        _open = [.. (await CallAsync("eplan_project_list", new() { ["limit"] = 200 })).GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("path").GetString()!)];
        Writable = RequireWritable ? WriteHost.OpenedBelow(_open, WriteHost.ScratchFolder, WriteHost.WritableName) : string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_host is not null)
        {
            await _host.StopAsync(CancellationToken.None);
            if (_host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync(); // the Host's BridgeClient is only IAsyncDisposable
            }
            else
            {
                _host.Dispose();
            }
        }

        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // EPLAN may still hold a file of a backup copy; the folder is in the system temp directory.
        }
    }

    /// <summary>The structured result of a successful call; a failed call fails the test with the error.</summary>
    public async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?> args)
    {
        CallToolResult result = await _client!.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError != true, $"{tool} failed: {WriteHost.Text(result)}");
        return result.StructuredContent!.Value;
    }

    /// <summary>The <c>error</c> object of a call that must fail.</summary>
    public async Task<JsonElement> ErrorAsync(string tool, Dictionary<string, object?> args)
    {
        CallToolResult result = await _client!.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError, $"{tool} succeeded where it should fail: {WriteHost.Text(result)}");
        return JsonDocument.Parse(WriteHost.Text(result)).RootElement.GetProperty("error");
    }

    /// <summary>Every record of this Host's audit log, oldest first.</summary>
    public List<JsonObject> AuditRecords() => WriteHost.AuditRecordsIn(Home);

    /// <summary>The records of one write, by the request ID the Host gave it.</summary>
    public List<JsonObject> AuditOf(string requestId) =>
        [.. AuditRecords().Where(r => r["requestId"]?.GetValue<string>() == requestId)];

    /// <summary>Every backup folder below this Host's backup root (<c>&lt;project&gt;\&lt;UTC time&gt;</c>), sorted.</summary>
    public string[] BackupFolders()
    {
        string root = new HostDataLocations(Home).Backups;
        return Directory.Exists(root)
            ? [.. Directory.EnumerateDirectories(root).SelectMany(Directory.EnumerateDirectories).Order(StringComparer.OrdinalIgnoreCase)]
            : [];
    }

    /// <summary>Whether the copy has a page with exactly this name.</summary>
    public async Task<bool> HasPageAsync(string page)
    {
        CallToolResult result = await _client!.CallToolAsync("eplan_page_get", new Dictionary<string, object?> { ["project"] = Writable, ["page"] = page }, cancellationToken: TestContext.Current.CancellationToken);
        if (result.IsError != true)
        {
            return result.StructuredContent!.Value.GetProperty("page").GetProperty("name").GetString() == page;
        }

        string reason = JsonDocument.Parse(WriteHost.Text(result)).RootElement.GetProperty("error").GetProperty("reason").GetString()!;
        return reason == "PAGE_NOT_FOUND" ? false : throw new InvalidOperationException("eplan_page_get failed: " + WriteHost.Text(result));
    }

    /// <summary>
    /// New, empty pages after the last numbered page of <see cref="Prefix"/> in the copy, created in one call, so a run
    /// never meets an earlier run's pages.
    /// </summary>
    public async Task<string[]> NewPagesAsync(int count)
    {
        int last = 0;
        string? cursor = null;
        do
        {
            Dictionary<string, object?> args = new() { ["project"] = Writable, ["name_prefix"] = Prefix, ["limit"] = 200 };
            if (cursor is not null)
            {
                args["cursor"] = cursor;
            }

            JsonElement list = await CallAsync("eplan_page_list", args);
            last = list.GetProperty("pages").EnumerateArray().Select(p => Number(p.GetProperty("name").GetString()!)).Append(last).Max();
            cursor = list.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        string[] names = [.. Enumerable.Range(last + 1, count).Select(n => Prefix + n.ToString(CultureInfo.InvariantCulture))];
        await CallAsync("eplan_page_create", new() { ["project"] = Writable, ["pages"] = Json(names.Select(name => new { name })) });
        return names;
    }

    /// <summary>
    /// The sample contactor feedback control with <c>number</c> on <paramref name="page"/>, as MacroInsertTests inserts
    /// it: EPLAN leaves the contact without its coil as <c>=GAA+A1-?QA…</c> (A14, A19).
    /// </summary>
    public Task<JsonElement> InsertMacroAsync(string page) => CallAsync("eplan_macro_insert", new()
    {
        ["project"] = Writable,
        ["page"] = page,
        ["macro"] = MacroTests.FeedbackControl,
        ["x"] = 40,
        ["y"] = 280,
        ["numbering"] = "number",
    });

    /// <summary>
    /// Deletes <paramref name="pages"/> of the copy as an engineer would: a dry run, the approval of exactly the change
    /// list it describes, then the deletion, which must commit. Returns the dry run and the deletion.
    /// </summary>
    public async Task<(JsonElement DryRun, JsonElement Deleted)> DeleteAsync(IReadOnlyCollection<string> pages)
    {
        Dictionary<string, object?> args = new() { ["project"] = Writable, ["pages"] = Json(pages) };
        JsonElement dryRun = await CallAsync("eplan_page_delete", new(args) { ["dry_run"] = true });
        Approvals.Approve("eplan_page_delete", Writable, dryRun.GetProperty("pages").EnumerateArray().Select(DeleteLine));
        JsonElement deleted = await CallAsync("eplan_page_delete", args);
        Assert.Equal("committed", deleted.GetProperty("execution").GetString());
        return (dryRun, deleted);
    }

    /// <summary>
    /// After a test failed: deletes those of <paramref name="pages"/> the copy still has, so the next run does not meet
    /// them (the page renumbering needs /200 to /220 free). Never throws over the test's own failure; says what it did.
    /// </summary>
    public async Task RemoveQuietlyAsync(IEnumerable<string> pages)
    {
        try
        {
            List<string> left = [];
            foreach (string page in pages.Distinct(StringComparer.Ordinal))
            {
                if (await HasPageAsync(page))
                {
                    left.Add(page);
                }
            }

            if (left.Count > 0)
            {
                await DeleteAsync(left);
            }

            TestContext.Current.TestOutputHelper?.WriteLine($"Clean-up after the failure: deleted [{string.Join(", ", left)}].");
        }
        catch (Exception ex)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Clean-up after the failure did not finish; delete these pages of {Writable} by hand: {string.Join(", ", pages)}. {ex.Message}");
        }
    }

    /// <summary>
    /// The line the approval shows for deleting a page as a dry run lists it (WritePipeline.ChangeLines and
    /// page.delete.plan): <c>delete page =GAA+A1&amp;EFS1/60 (Circuit, 19 functions)</c>.
    /// </summary>
    public static string DeleteLine(JsonElement page)
    {
        int functions = page.GetProperty("functions").GetInt32();
        return $"delete page {page.GetProperty("name").GetString()} ({page.GetProperty("type").GetString()}, {functions.ToString(CultureInfo.InvariantCulture)} {(functions == 1 ? "function" : "functions")})";
    }

    /// <summary>The page number of a page of <see cref="Prefix"/>; 0 for any other name.</summary>
    public static int Number(string page) =>
        page.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(page[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0;

    public static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
