using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_page_delete on the write copy through <see cref="DestructiveHost"/> (docs/tools/eplan_page_delete.md, sections 6
/// and 7): the dry run, a denied and an approved deletion with its backup and audit, the policy outside scratch. Only new
/// pages are deleted; the sample project's own pages are only planned, so the copy stays as the other tests need it.
/// </summary>
[Trait("Size", "Large")]
public sealed class PageDeleteTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_page_delete";
    private const string Circuit1 = "=GAA+A1&EFS1/1";

    private readonly DestructiveHost _host;

    public PageDeleteTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task DryRun_OfTheFirstCircuitPage_ListsItsFunctionsAndTheCablesLeftWithoutTheirMainFunction()
    {
        int asked = _host.Approvals.Questions.Count;

        JsonElement dryRun = await _host.CallAsync(Tool, new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { Circuit1 }), ["dry_run"] = true });

        Assert.Equal(
            ("not_started", true, JsonValueKind.Null, JsonValueKind.Null),
            (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("dryRun").GetBoolean(), dryRun.GetProperty("backup").ValueKind, dryRun.GetProperty("undo").ValueKind));
        JsonElement page = Assert.Single(dryRun.GetProperty("pages").EnumerateArray());
        Assert.Equal(Circuit1, page.GetProperty("name").GetString());
        Assert.InRange(page.GetProperty("functions").GetInt32(), 1, 500);

        // S12 (A18): four cables have their main function on this page. The tool lists the devices whose functions left
        // on other pages lose their main function; +A2-WE2's other function is on no page, so it is not listed (A20).
        JsonElement[] orphaned = [.. dryRun.GetProperty("orphaned").EnumerateArray()];
        Assert.DoesNotContain(orphaned, o => o.GetProperty("tag").GetString() == "+A2-WE2");
        foreach (string cable in new[] { "+A1-WE1", "+A1-WE2", "+A2-WE1" })
        {
            JsonElement device = Assert.Single(orphaned, o => o.GetProperty("tag").GetString() == cable);
            Assert.InRange(device.GetProperty("functionsLeft").GetInt32(), 1, 100);
            Assert.DoesNotContain(Circuit1, device.GetProperty("pages").EnumerateArray().Select(p => p.GetString()));
        }

        Assert.Contains("eplan_report_update", dryRun.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal(asked, _host.Approvals.Questions.Count); // a dry run asks nobody
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{Circuit1}: {page.GetProperty("type").GetString()}, {page.GetProperty("functions").GetInt32()} functions; orphaned: "
            + string.Join("; ", orphaned.Select(o => $"{o.GetProperty("tag").GetString()} {o.GetProperty("functionsLeft").GetInt32()} on {string.Join(", ", o.GetProperty("pages").EnumerateArray().Select(p => p.GetString()))}")));
    }

    [Fact]
    public async Task ANewMacroPage_StaysWhenDenied_IsBackedUpAndDeletedWhenApproved_AndThenIsNotFound()
    {
        string page = (await _host.NewPagesAsync(1))[0];
        try
        {
            await _host.InsertMacroAsync(page);
            Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { page }) };
            JsonElement dryRun = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
            JsonElement planned = Assert.Single(dryRun.GetProperty("pages").EnumerateArray());
            Assert.InRange(planned.GetProperty("functions").GetInt32(), 1, 100);

            // Denied: nothing is backed up or changed (A20), and the refusal is audited without an execution.
            string[] folders = _host.BackupFolders();
            JsonElement denied = await _host.ErrorAsync(Tool, args);
            Assert.Equal(("APPROVAL_DENIED", "not_started"), (denied.GetProperty("reason").GetString(), denied.GetProperty("execution").GetString()));
            Assert.Equal(folders, _host.BackupFolders());
            Assert.True(await _host.HasPageAsync(page));
            List<JsonObject> refusal = _host.AuditOf(_host.Approvals.Questions[^1].RequestId);
            Assert.Equal([("approval", "denied")], refusal.Select(r => (r["kind"]!.GetValue<string>(), r["decision"]?.GetValue<string>())));

            // Approved: the backup comes first, into a new folder of this Host's data root, then the page goes.
            _host.Approvals.Approve(Tool, _host.Writable, [DestructiveHost.DeleteLine(planned)]);
            JsonElement deleted = await _host.CallAsync(Tool, args);
            Assert.Equal(("committed", "AI: eplan_page_delete"), (deleted.GetProperty("execution").GetString(), deleted.GetProperty("undo").GetString()));
            Assert.Equal(page, Assert.Single(deleted.GetProperty("pages").EnumerateArray()).GetProperty("name").GetString());
            string backup = deleted.GetProperty("backup").GetString()!;
            string folder = Assert.Single(_host.BackupFolders().Except(folders));
            Assert.Equal(Path.Combine(folder, Path.GetFileName(_host.Writable) + ".elk"), backup, ignoreCase: true);

            // The Bridge counted the copy's pages against the project's before it deleted anything (ADR-0013).
            Assert.True(File.Exists(backup) && Directory.Exists(Path.ChangeExtension(backup, ".edb")), "No backup copy at " + backup);
            Assert.False(await _host.HasPageAsync(page));
            List<JsonObject> records = _host.AuditOf(_host.Approvals.Questions[^1].RequestId);
            Assert.Equal(
                [("approval", null), ("execution_started", null), ("execution_result", "committed")],
                records.Select(r => (r["kind"]!.GetValue<string>(), r["execution"]?.GetValue<string>())));
            Assert.Equal((ScriptedApprovalChannel.ChannelName, "granted"), (records[0]["channel"]!.GetValue<string>(), records[0]["decision"]!.GetValue<string>()));
            Assert.Equal(backup, records[1]["backup"]!.GetValue<string>());
            Assert.True(records[2]["backupCompleted"]!.GetValue<bool>());

            // Not idempotent: the page is gone, so the plan already fails and nobody is asked (tool spec 6).
            int asked = _host.Approvals.Questions.Count;
            JsonElement again = await _host.ErrorAsync(Tool, args);
            Assert.Equal(("PAGE_NOT_FOUND", "not_started", page), (again.GetProperty("reason").GetString(), again.GetProperty("execution").GetString(), again.GetProperty("metadata").GetProperty("missing").GetString()));
            Assert.Equal(asked, _host.Approvals.Questions.Count);
        }
        catch
        {
            await _host.RemoveQuietlyAsync([page]);
            throw;
        }
    }

    [Fact]
    public async Task ThreeNewPages_AreDeletedInOneCall()
    {
        string[] pages = await _host.NewPagesAsync(3);
        try
        {
            (JsonElement dryRun, JsonElement deleted) = await _host.DeleteAsync(pages);

            Assert.Equal(pages, dryRun.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
            Assert.Equal(pages, deleted.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
            Assert.Empty(deleted.GetProperty("orphaned").EnumerateArray());
            foreach (string page in pages)
            {
                Assert.False(await _host.HasPageAsync(page), page);
            }
        }
        catch
        {
            await _host.RemoveQuietlyAsync(pages);
            throw;
        }
    }

    [Fact]
    public async Task TheCopyOutsideScratch_IsPolicyDenied_WithoutAskingOrBackingUp()
    {
        string project = _host.Outside;
        Dictionary<string, (long Size, DateTime Written)> before = NoSideEffectTests.ProjectFiles(project, withUserData: false);
        int asked = _host.Approvals.Questions.Count;
        string[] folders = _host.BackupFolders();

        JsonElement error = await _host.ErrorAsync(Tool, new() { ["project"] = project, ["pages"] = DestructiveHost.Json(new[] { Circuit1 }) });

        // ADR-0005: Destructive outside scratch with writesOutsideScratch "deny" is refused before anyone is asked.
        Assert.Equal(("POLICY_DENIED", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(asked, _host.Approvals.Questions.Count);
        Assert.Equal(folders, _host.BackupFolders());
        Assert.Equal(before, NoSideEffectTests.ProjectFiles(project, withUserData: false));
    }
}
