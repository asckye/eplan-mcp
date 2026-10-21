using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_terminal_renumber on the write copy through <see cref="DestructiveHost"/> (docs/tools/eplan_terminal_renumber.md,
/// section 7). Unlike the other Destructive tests these change the copy for good: numbering +A1-XD1 puts its terminals in
/// order, as S21 saw (api-notes A24), so only the first run on a fresh copy renames them and later runs find nothing to
/// rename; both are checked. The numbering of the '?' terminals is on a new macro page, deleted again.
/// </summary>
[Trait("Size", "Large")]
public sealed class TerminalRenumberTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_terminal_renumber";
    private const string Strip = "+A1-XD1";
    private const string Untouched = "+A1-XD4";
    private const string Scheme = "Numerisch";

    private readonly DestructiveHost _host;

    public TerminalRenumberTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task OneStrip_AllTerminals_RenamesOnlyThatStrip_InItsOrder()
    {
        string[] before = await NamesAsync(Strip);
        string[] other = await NamesAsync(Untouched);
        Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["strips"] = DestructiveHost.Json(new[] { Strip }), ["scheme"] = Scheme, ["only_unnumbered"] = false };

        JsonElement dryRun = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        JsonElement scope = dryRun.GetProperty("scope");
        Assert.Equal(("not_started", 1, before.Length, JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), scope.GetProperty("stripCount").GetInt32(), scope.GetProperty("terminals").GetInt32(), dryRun.GetProperty("changed").ValueKind));

        _host.Approvals.Approve(Tool, _host.Writable, [$"renumber all terminal designations on 1 strip ({before.Length} terminals), scheme: {Scheme}, start 1, step 1"]);
        JsonElement numbered = await _host.CallAsync(Tool, args);
        string[] after = await NamesAsync(Strip);
        (string From, string To)[] changed = [.. numbered.GetProperty("changed").EnumerateArray().Select(c => (c.GetProperty("from").GetString()!, c.GetProperty("to").GetString()!))];
        TestContext.Current.TestOutputHelper?.WriteLine($"{Strip} before: {string.Join(", ", before)}; after: {string.Join(", ", after)}; changed: {string.Join(", ", changed.Select(c => c.From + " -> " + c.To))}.");

        // The strip's terminals took each other's numbers (S21: :2 -> :3, :3 -> :5, :4 -> :2, :5 -> :4), and no other strip changed.
        Assert.Equal(before.Order(StringComparer.Ordinal), after.Order(StringComparer.Ordinal));
        Assert.All(changed, c => Assert.True(c.From.StartsWith(Strip + ":", StringComparison.Ordinal) && c.To.StartsWith(Strip + ":", StringComparison.Ordinal), c.ToString()));
        Assert.Equal(changed.Length, numbered.GetProperty("changedCount").GetInt32());
        Assert.Equal(other, await NamesAsync(Untouched));
        Assert.True(File.Exists(numbered.GetProperty("backup").GetString()), "No backup copy at " + numbered.GetProperty("backup"));
        if (changed.Length > 0)
        {
            Assert.Equal(("committed", "AI: " + Tool), (numbered.GetProperty("execution").GetString(), numbered.GetProperty("undo").GetString()));
        }
        else
        {
            // A run after the first on this copy: the strip is in order already, EPLAN renames nothing.
            Assert.Equal(("not_started", JsonValueKind.Null), (numbered.GetProperty("execution").GetString(), numbered.GetProperty("undo").ValueKind));
        }
    }

    [Fact]
    public async Task OnlyUnnumbered_AfterAMacroInsertedWithNumber_NumbersOnlyTheQuestionMarks()
    {
        string page = (await _host.NewPagesAsync(1))[0];
        try
        {
            // EPLAN leaves the macro's terminals as +A1-XG…:?1 (A20); the whole project, as the sample has no strip of its own with '?'.
            await _host.InsertMacroAsync(page);
            Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["scheme"] = Scheme };
            int terminals = (await _host.CallAsync(Tool, new(args) { ["dry_run"] = true })).GetProperty("scope").GetProperty("terminals").GetInt32();
            Assert.InRange(terminals, 1, 200);

            _host.Approvals.Approve(Tool, _host.Writable, [$"renumber terminal designations containing '?' in the project ({terminals} {(terminals == 1 ? "terminal" : "terminals")}), scheme: {Scheme}, start 1, step 1"]);
            JsonElement numbered = await _host.CallAsync(Tool, args);
            TestContext.Current.TestOutputHelper?.WriteLine(numbered.ToString());

            // EPLAN is still there, and it renamed only terminals whose designation had '?' (tool spec 5, to be confirmed).
            JsonElement status = await _host.CallAsync("eplan_session_status", []);
            Assert.Equal((true, _host.ProcessId), (status.GetProperty("connected").GetBoolean(), status.GetProperty("eplan").GetProperty("processId").GetInt32()));
            JsonElement[] changed = [.. numbered.GetProperty("changed").EnumerateArray()];
            Assert.NotEmpty(changed);
            Assert.All(changed, c => Assert.Contains('?', c.GetProperty("from").GetString()!.Split(':')[^1]));
            Assert.Equal("committed", numbered.GetProperty("execution").GetString());

            await _host.DeleteAsync([page]);
        }
        catch
        {
            await _host.RemoveQuietlyAsync([page]);
            throw;
        }
    }

    [Fact]
    public async Task ASchemeEplanDoesNotOffer_IsRefusedBeforeTheApproval_WithTheOfferedOnes()
    {
        int folders = _host.BackupFolders().Length;
        int asked = _host.Approvals.Questions.Count;

        JsonElement error = await _host.ErrorAsync(Tool, new() { ["project"] = _host.Writable, ["strips"] = DestructiveHost.Json(new[] { Strip }), ["scheme"] = "numerisch" });

        // S21 found Gerätedefinition, Initiatoren and Numerisch under USER.Terminal.Numbering.Scheme (A24).
        string[] candidates = error.GetProperty("metadata").GetProperty("candidates").GetString()!.Split('\n');
        Assert.Equal(("INVALID_ARGUMENT", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Contains(Scheme, candidates);
        Assert.DoesNotContain("InternalRefDefScheme", candidates);
        Assert.Equal((asked, folders), (_host.Approvals.Questions.Count, _host.BackupFolders().Length));
    }

    /// <summary>The terminals' full names of a strip of the copy, in the strip's order.</summary>
    private async Task<string[]> NamesAsync(string strip)
    {
        JsonElement data = await _host.CallAsync("eplan_terminal_strip_get", new() { ["project"] = _host.Writable, ["strip"] = strip, ["limit"] = 200 });
        return [.. data.GetProperty("terminals").EnumerateArray().Select(t => t.GetProperty("name").GetString()!)];
    }
}
