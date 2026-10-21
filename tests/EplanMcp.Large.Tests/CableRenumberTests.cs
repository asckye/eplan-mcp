using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_cable_renumber on the write copy through <see cref="DestructiveHost"/> (docs/tools/eplan_cable_renumber.md,
/// section 7). These change the copy for good: from the sample project's tags, numbering every cable in "Standard", the
/// project's only scheme and so the one S21's empty scheme ran, swapped +A2-WD4 with +A2-WD5 and +B2.Y1-WD1 with
/// +B2.Y1-WD2 (api-notes A24). The tests check what holds on every run and write the changes to the output.
/// </summary>
[Trait("Size", "Large")]
public sealed class CableRenumberTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_cable_renumber";
    private const string Scheme = "Standard";

    /// <summary>
    /// A real cable that numbering in Standard leaves alone and no other write test renames: CableTests reads it as a cable
    /// with its two ends and two cores (live, 2026-10-21), and numbering every cable in Standard renamed only +A2-WD4,
    /// +A2-WD5, +B2.Y1-WD1 and +B2.Y1-WD2 (S21; live, 2026-10-21). Not +A1-WE1: its category is BusBarDefText.
    /// </summary>
    private const string Kept = "+B1-WG2";

    private static readonly string[] _kept = [Kept];
    private static readonly string[] _breaker = ["=GAA+A1-FC1"];

    private readonly DestructiveHost _host;

    public CableRenumberTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task EveryCable_InStandard_KeepsTheTagsUnique_AndLeavesTheOthersAlone()
    {
        string[] before = await CableTagsAsync();
        Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["scheme"] = Scheme };

        JsonElement dryRun = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        int cables = dryRun.GetProperty("scope").GetProperty("cables").GetInt32();
        Assert.Equal(("not_started", JsonValueKind.Null, JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("scope").GetProperty("cableCount").ValueKind, dryRun.GetProperty("changed").ValueKind));
        Assert.InRange(cables, 1, 200);

        _host.Approvals.Approve(Tool, _host.Writable, [$"renumber all cable tags in the project ({cables} cables), scheme: {Scheme}, start 1, step 1"]);
        JsonElement numbered = TerminalRenumberTests.Answered(await _host.CallAsync(Tool, args));
        string[] after = await CableTagsAsync();
        (string From, string To)[] changed = [.. numbered.GetProperty("changed").EnumerateArray().Select(c => (c.GetProperty("from").GetString()!, c.GetProperty("to").GetString()!))];
        TestContext.Current.TestOutputHelper?.WriteLine($"{cables} cables planned, {before.Length} listed; changed: {string.Join(", ", changed.Select(c => c.From + " -> " + c.To))} (S21: +A2-WD4 <-> +A2-WD5, +B2.Y1-WD1 <-> +B2.Y1-WD2).");

        Assert.Equal(after.Length, after.Distinct(StringComparer.Ordinal).Count());
        Assert.All(changed, c => Assert.Contains(c.From, before));
        Assert.Contains(Kept, after);
        Assert.DoesNotContain(changed, c => c.From == Kept);
        Assert.Equal(changed.Length, numbered.GetProperty("changedCount").GetInt32());
        Assert.True(File.Exists(numbered.GetProperty("backup").GetString()), "No backup copy at " + numbered.GetProperty("backup"));
        Assert.Equal(changed.Length > 0 ? "committed" : "not_started", numbered.GetProperty("execution").GetString());
        Assert.Equal(changed.Length > 0 ? "AI: " + Tool : null, numbered.GetProperty("undo").GetString());
    }

    [Fact]
    public async Task TwoGivenCables_LeaveTheOthersAlone()
    {
        // +A2-WD4 and +A2-WD5 on a fresh copy; whatever two +A2-WD cables there are after an earlier run renamed them.
        string[] before = await CableTagsAsync();
        string[] given = [.. before.Where(t => t.StartsWith("+A2-WD", StringComparison.Ordinal)).Concat(before).Distinct(StringComparer.Ordinal).Take(2)];
        Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["cables"] = DestructiveHost.Json(given), ["scheme"] = Scheme };

        _host.Approvals.Approve(Tool, _host.Writable, [$"renumber all cable tags of the given cables (2 cables), scheme: {Scheme}, start 1, step 1"]);
        JsonElement numbered = TerminalRenumberTests.Answered(await _host.CallAsync(Tool, args));
        string[] after = await CableTagsAsync();
        TestContext.Current.TestOutputHelper?.WriteLine(numbered.GetProperty("changed").ToString());

        // Which tags EPLAN gives two of the project's cables is to be seen (tool spec 5); the other cables keep theirs.
        Assert.Equal(before.Except(given).Order(StringComparer.Ordinal), after.Where(before.Contains).Except(given).Order(StringComparer.Ordinal));
        Assert.All(numbered.GetProperty("changed").EnumerateArray(), c => Assert.Contains(c.GetProperty("from").GetString(), given));
        Assert.Equal(after.Length, after.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task KeepExisting_WithoutQuestionMarks_HasNothingToNumber_AndAsksNobody()
    {
        int asked = _host.Approvals.Questions.Count;

        JsonElement result = await _host.CallAsync(Tool, new() { ["project"] = _host.Writable, ["cables"] = DestructiveHost.Json(_kept), ["scheme"] = Scheme, ["keep_existing"] = true });

        Assert.Equal(("not_started", 0, JsonValueKind.Null), (result.GetProperty("execution").GetString(), result.GetProperty("scope").GetProperty("cables").GetInt32(), result.GetProperty("backup").ValueKind));
        Assert.Equal(asked, _host.Approvals.Questions.Count);
    }

    [Fact]
    public async Task ABreakerAndASchemeTheProjectDoesNotOffer_AreRefusedBeforeTheApproval()
    {
        int asked = _host.Approvals.Questions.Count;
        int folders = _host.BackupFolders().Length;

        JsonElement breaker = await _host.ErrorAsync(Tool, new() { ["project"] = _host.Writable, ["cables"] = DestructiveHost.Json(_breaker), ["scheme"] = Scheme });
        JsonElement scheme = await _host.ErrorAsync(Tool, new() { ["project"] = _host.Writable, ["scheme"] = "standard" });

        // The project's cable numbering schemes, read from CableLog.CableNumberingScheme: S21 found only Standard (A24).
        string[] candidates = scheme.GetProperty("metadata").GetProperty("candidates").GetString()!.Split('\n');
        TestContext.Current.TestOutputHelper?.WriteLine("Cable numbering schemes: " + string.Join(", ", candidates));
        Assert.Equal(("INVALID_ARGUMENT", "not_started", "=GAA+A1-FC1 (CircuitBreaker)"), (breaker.GetProperty("reason").GetString(), breaker.GetProperty("execution").GetString(), breaker.GetProperty("metadata").GetProperty("notCables").GetString()));
        Assert.Equal(("INVALID_ARGUMENT", "not_started"), (scheme.GetProperty("reason").GetString(), scheme.GetProperty("execution").GetString()));
        Assert.Contains(Scheme, candidates);
        Assert.DoesNotContain("InternalRefDefScheme", candidates);
        Assert.Equal((asked, folders), (_host.Approvals.Questions.Count, _host.BackupFolders().Length));
    }

    /// <summary>The full tags of the copy's cables, as eplan_device_list gives them with category Cable.</summary>
    private async Task<string[]> CableTagsAsync()
    {
        JsonElement listed = await _host.CallAsync("eplan_device_list", new() { ["project"] = _host.Writable, ["category"] = "Cable", ["limit"] = 200 });
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("nextCursor").ValueKind);
        return [.. listed.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!)];
    }
}
