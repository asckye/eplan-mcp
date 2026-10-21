using System.Globalization;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_connection_number on the write copy through <see cref="DestructiveHost"/> (docs/tools/eplan_connection_number.md,
/// section 7). This changes the copy's wire numbers for good: on the sample project's, overwriting all of them in
/// "Standard" gave 605 connections a new one, 116 → 659 numbered (S21, api-notes A24). The test checks what holds on
/// every run, writes the numbers to the output, and then records what except_manual does.
/// </summary>
[Trait("Size", "Large")]
public sealed class ConnectionNumberTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_connection_number";
    private const string Scheme = "Standard";

    private readonly DestructiveHost _host;

    public ConnectionNumberTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task Standard_OverwritingAll_ThenExceptManual()
    {
        JsonElement all = await NumberAsync("all");
        int designated = all.GetProperty("designatedAfter").GetInt32();
        int changed = all.GetProperty("changedCount").GetInt32();
        JsonElement[] listed = [.. all.GetProperty("changed").EnumerateArray()];

        // S21 on a fresh copy: 605 changed, 659 numbered afterwards; a later run gives the same numbers again.
        Assert.InRange(designated, all.GetProperty("scope").GetProperty("designatedBefore").GetInt32(), all.GetProperty("scope").GetProperty("connections").GetInt32());
        Assert.Equal((Math.Min(changed, 200), changed > 200), (listed.Length, all.GetProperty("truncated").GetBoolean()));
        Assert.All(listed, c => Assert.NotEqual(c.GetProperty("from").GetString(), c.GetProperty("to").GetString()));

        JsonElement exceptManual = await NumberAsync("except_manual");
        Assert.Equal(designated, exceptManual.GetProperty("scope").GetProperty("designatedBefore").GetInt32());
    }

    [Fact]
    public async Task ASchemeTheProjectDoesNotOffer_IsRefusedBeforeTheApproval_WithTheProjectsSchemes()
    {
        int asked = _host.Approvals.Questions.Count;
        int folders = _host.BackupFolders().Length;

        JsonElement error = await _host.ErrorAsync(Tool, new() { ["project"] = _host.Writable, ["scheme"] = "Potential" });

        // S21 found 11 schemes under the project setting WireNumberingLog.Schema (A24).
        string[] candidates = error.GetProperty("metadata").GetProperty("candidates").GetString()!.Split('\n');
        TestContext.Current.TestOutputHelper?.WriteLine($"{candidates.Length} wire numbering schemes: {string.Join(", ", candidates)}");
        Assert.Equal(("INVALID_ARGUMENT", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Contains(Scheme, candidates);
        Assert.Contains("Potenzialorientiert", candidates);
        Assert.DoesNotContain("InternalRefDefScheme", candidates);
        Assert.Equal((asked, folders), (_host.Approvals.Questions.Count, _host.BackupFolders().Length));
    }

    /// <summary>A dry run, the approval of exactly its change line, the numbering; checks the backup and the execution.</summary>
    private async Task<JsonElement> NumberAsync(string overwrite)
    {
        Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["scheme"] = Scheme, ["overwrite"] = overwrite };
        JsonElement scope = (await _host.CallAsync(Tool, new(args) { ["dry_run"] = true })).GetProperty("scope");
        int connections = scope.GetProperty("connections").GetInt32();
        int before = scope.GetProperty("designatedBefore").GetInt32();
        Assert.InRange(connections, 1, int.MaxValue);

        _host.Approvals.Approve(Tool, _host.Writable, [string.Create(CultureInfo.InvariantCulture, $"renumber wire numbers in the project ({connections} connections, {before} numbered), scheme: {Scheme}, overwrite: {overwrite.Replace('_', ' ')}")]);
        JsonElement numbered = TerminalRenumberTests.Answered(await _host.CallAsync(Tool, args));
        int changed = numbered.GetProperty("changedCount").GetInt32();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{overwrite}: {connections} connections, {before} -> {numbered.GetProperty("designatedAfter")} numbered, {changed} changed; first: "
            + string.Join(", ", numbered.GetProperty("changed").EnumerateArray().Take(12).Select(c => $"{c.GetProperty("from")} -> {c.GetProperty("to")} ({c.GetProperty("page")})")));

        Assert.True(File.Exists(numbered.GetProperty("backup").GetString()), "No backup copy at " + numbered.GetProperty("backup"));
        Assert.Equal(changed > 0 ? "committed" : "not_started", numbered.GetProperty("execution").GetString());
        Assert.Equal(changed > 0 ? "AI: " + Tool : null, numbered.GetProperty("undo").GetString());
        return numbered;
    }
}
