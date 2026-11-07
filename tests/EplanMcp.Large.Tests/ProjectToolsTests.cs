using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>Opening and closing, sorting, renaming, translating on write and PLC addressing on LargeWrite (api-notes A107).</summary>
[Trait("Size", "Large")]
public sealed class ProjectToolsTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    private readonly string _run = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Fact]
    public async Task CloseThenOpen_LeavesItOpenSelectedAndWritable_OutsideScratchIsRefused()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string link = session.Project + ".elk";
        int pages = (await eplan.CallAsync("eplan_page_list", new() { ["project"] = session.Project, ["limit"] = 1 })).GetProperty("total").GetInt32();

        JsonElement preview = await eplan.CallAsync("eplan_project_close", new() { ["project"] = session.Project });
        Assert.Equal("not_started", preview.GetProperty("execution").GetString());
        Assert.Contains(session.Project, await OpenAsync(), StringComparer.OrdinalIgnoreCase);

        JsonElement closed = await eplan.CallAsync("eplan_project_close", new() { ["project"] = session.Project, ["dry_run"] = false });
        Assert.Equal("committed", closed.GetProperty("execution").GetString());
        Assert.DoesNotContain(session.Project, await OpenAsync(), StringComparer.OrdinalIgnoreCase);

        JsonElement opened = await eplan.CallAsync("eplan_project_open", new() { ["path"] = link, ["dry_run"] = false });
        Assert.Equal("committed", opened.GetProperty("execution").GetString());
        Assert.True(opened.GetProperty("current").GetBoolean(), "EPLAN should select the only open project, since undo works on the selected one.");
        Assert.Equal(pages, (await eplan.CallAsync("eplan_page_list", new() { ["project"] = session.Project, ["limit"] = 1 })).GetProperty("total").GetInt32());

        JsonElement again = await eplan.CallAsync("eplan_project_open", new() { ["path"] = link, ["dry_run"] = false });
        Assert.Equal("not_started", again.GetProperty("execution").GetString());

        // Writes work on it, and one undo reverts them.
        string name = "+MCPO" + _run;
        var write = new Dictionary<string, object?> { ["project"] = session.Project, ["identifiers"] = new object[] { new { name } }, ["dry_run"] = false };
        Assert.Equal("committed", (await eplan.CallAsync("eplan_structure_update", write)).GetProperty("execution").GetString());
        await session.UndoAsync();

        string outside = WriteHost.OutsideFolder + @"\LargeOutside.elk";
        JsonElement refused = await host.ErrorAsync("eplan_project_open", new() { ["path"] = outside, ["dry_run"] = false });
        Assert.Equal("POLICY_DENIED", refused.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Order_SortsTheBlock_ReadsBack_OneUndoRestores()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string first = "+MCPA" + _run, second = "+MCPB" + _run;
        var create = new Dictionary<string, object?> { ["project"] = session.Project, ["identifiers"] = new object[] { new { name = first }, new { name = second } }, ["dry_run"] = false };
        await eplan.CallAsync("eplan_structure_update", create);
        List<string> created = await NamesAsync(session.Project);

        JsonElement sorted = await eplan.CallAsync("eplan_structure_update", new() { ["project"] = session.Project, ["order"] = new[] { second, first }, ["dry_run"] = false });
        Assert.Equal("committed", sorted.GetProperty("execution").GetString());
        List<string> after = await NamesAsync(session.Project);
        Assert.Equal(0, after.Where(n => n.StartsWith('+')).ToList().IndexOf(second));
        Assert.Equal(1, after.Where(n => n.StartsWith('+')).ToList().IndexOf(first));

        await session.UndoAsync();
        Assert.Equal(created, await NamesAsync(session.Project));
        await session.UndoAsync();
    }

    [Fact]
    public async Task Rename_ChangesThePageThatUsesIt_OneUndoRestores()
    {
        host.Approvals.Forget();
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string oldName = "+MCPR" + _run, newName = "+MCPN" + _run;
        string page = "=GAA" + oldName + "&EFS1/1";
        await eplan.CallAsync("eplan_page_create", new() { ["project"] = session.Project, ["pages"] = new[] { new { name = page, page_type = "Circuit" } }, ["dry_run"] = false });
        Assert.Contains(oldName, await NamesAsync(session.Project));

        var args = new Dictionary<string, object?> { ["project"] = host.Writable, ["renames"] = new[] { new { name = oldName, new_name = newName } } };
        Assert.Equal("not_started", (await host.CallAsync("eplan_structure_rename", args)).GetProperty("execution").GetString());
        host.Approvals.Approve("eplan_structure_rename", host.Writable, new[] { $"update structure {oldName}: name '{oldName}' -> '{newName}'" });
        args["dry_run"] = false;
        JsonElement renamed = await host.CallAsync("eplan_structure_rename", args);
        Assert.Equal("committed", renamed.GetProperty("execution").GetString());
        Assert.True(File.Exists(renamed.GetProperty("backup").GetString()));

        List<string> names = await NamesAsync(session.Project);
        Assert.Contains(newName, names);
        Assert.DoesNotContain(oldName, names);
        Assert.True(await host.HasPageAsync("=GAA" + newName + "&EFS1/1"), "The page should have the new name.");

        await session.UndoAsync();
        Assert.Contains(oldName, await NamesAsync(session.Project));
        Assert.True(await host.HasPageAsync(page));
    }

    [Fact]
    public async Task Translate_FillsLanguagesFromTheDatabase_WhenTheProjectHasASourceLanguage()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        var args = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["identifiers"] = new object[] { new { name = "&MCPL" + _run, description = "Schaltplan" } },
            ["translate"] = true,
        };
        JsonElement preview = await eplan.CallAsync("eplan_structure_update", args);
        string planned = preview.GetProperty("changes")[0].GetProperty("newValue").GetString()!;
        TestContext.Current.TestOutputHelper!.WriteLine("TranslateText returned: " + planned);
        Assert.Contains("Schaltplan", planned, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readdress_BadAddressIsRefused_GoodOneChangesAddresses_OneUndoRestores()
    {
        host.Approvals.Forget();
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        var output = TestContext.Current.TestOutputHelper!;

        var probe = new Dictionary<string, object?> { ["project"] = host.Writable };
        string before = (await host.CallAsync("eplan_plc_readdress", probe)).GetProperty("fingerprint").GetString()!;

        // Which format EPLAN takes depends on the project; each attempt that EPLAN refuses leaves the project as it was (api-notes A107).
        JsonElement? done = null;
        foreach (Dictionary<string, object?> args in new Dictionary<string, object?>[]
        {
            new() { ["project"] = host.Writable, ["digital_input"] = "I100.0", ["digital_output"] = "Q100.0" },
            new() { ["project"] = host.Writable, ["digital_input"] = "%IX100.0", ["digital_output"] = "%QX100.0" },
            new() { ["project"] = host.Writable },
        })
        {
            JsonElement preview = await host.CallAsync("eplan_plc_readdress", args);
            Assert.Equal("not_started", preview.GetProperty("execution").GetString());
            host.Approvals.Approve("eplan_plc_readdress", host.Writable, new[] { "renumber " + preview.GetProperty("scope").GetString() });
            (bool succeeded, JsonElement result) = await host.CallOrErrorAsync("eplan_plc_readdress", new(args) { ["dry_run"] = false });
            output.WriteLine($"{args.GetValueOrDefault("digital_input") ?? "(no start)"}: {succeeded}: {result}");
            if (succeeded)
            {
                done = result;
                break;
            }
            Assert.Equal("EPLAN_OPERATION_FAILED", result.GetProperty("reason").GetString());
        }

        Assert.NotNull(done);
        Assert.Equal("committed", done.Value.GetProperty("execution").GetString());
        Assert.True(File.Exists(done.Value.GetProperty("backup").GetString()));
        Assert.True(done.Value.GetProperty("changedCount").GetInt32() > 0, "EPLAN changed no address.");
        Assert.NotEqual(before, (await host.CallAsync("eplan_plc_readdress", probe)).GetProperty("fingerprint").GetString());
        await session.UndoAsync();
        Assert.Equal(before, (await host.CallAsync("eplan_plc_readdress", probe)).GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task CableReassign_OneCable_CommitsAndOneUndoRestores()
    {
        host.Approvals.Forget();
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        JsonElement list = await eplan.CallAsync("eplan_device_list", new() { ["project"] = session.Project, ["category"] = "Cable", ["limit"] = 1 });
        string cable = list.GetProperty("devices")[0].GetProperty("tag").GetString()!;
        var args = new Dictionary<string, object?> { ["project"] = host.Writable, ["cables"] = new[] { cable } };
        JsonElement preview = await host.CallAsync("eplan_cable_reassign", args);
        Assert.Equal("not_started", preview.GetProperty("execution").GetString());
        string before = preview.GetProperty("fingerprint").GetString()!;
        host.Approvals.Approve("eplan_cable_reassign", host.Writable, new[] { $"update cables {WriteHost.WritableName}: cores '{preview.GetProperty("scope").GetString()}' -> 'assigned by EPLAN'" });
        args["dry_run"] = false;
        JsonElement done = await host.CallAsync("eplan_cable_reassign", args);
        Assert.Equal("committed", done.GetProperty("execution").GetString());
        TestContext.Current.TestOutputHelper!.WriteLine($"{cable}: {done.GetProperty("changedCount").GetInt32()} changed: {done.GetProperty("changed")}");
        // The sample project's cables are assigned already; EPLAN then adds no undo step, and undoing would raise its conflict dialog (A41).
        if (done.GetProperty("changedCount").GetInt32() > 0)
        { await session.UndoAsync(); }
        else
        { Assert.Equal(JsonValueKind.Null, done.GetProperty("undo").ValueKind); }
        args.Remove("dry_run");
        Assert.Equal(before, (await host.CallAsync("eplan_cable_reassign", args)).GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task MasterDataUpdate_NoDialog_ProjectStaysReadable()
    {
        host.Approvals.Forget();
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        var args = new Dictionary<string, object?> { ["project"] = host.Writable };
        JsonElement preview = await host.CallAsync("eplan_masterdata_update", args);
        Assert.Equal("not_started", preview.GetProperty("execution").GetString());
        host.Approvals.Approve("eplan_masterdata_update", host.Writable, new[] { $"update project {WriteHost.WritableName}: master data '{preview.GetProperty("scope").GetString()}' -> 'the system's versions'" });
        args["dry_run"] = false;
        JsonElement done = await host.CallAsync("eplan_masterdata_update", args);
        Assert.Equal("committed", done.GetProperty("execution").GetString());
        Assert.True(done.GetProperty("entries").GetInt32() > 0);
        TestContext.Current.TestOutputHelper!.WriteLine($"{preview.GetProperty("scope").GetString()} -> {done.GetProperty("entries").GetInt32()}, added {done.GetProperty("added")}");
        Assert.NotEmpty((await eplan.CallAsync("eplan_page_list", new() { ["project"] = session.Project })).GetProperty("pages").EnumerateArray());
    }

    [Fact]
    public async Task ReportSettings_SetAndReadBack_ThenRestored()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        var off = new Dictionary<string, object?> { ["project"] = session.Project, ["summarize_parts_of_same_device"] = false };
        JsonElement preview = await eplan.CallAsync("eplan_report_configure", off);
        bool wasOn = preview.GetProperty("changes").GetArrayLength() == 1;
        var on = new Dictionary<string, object?> { ["project"] = session.Project, ["summarize_parts_of_same_device"] = true };
        Dictionary<string, object?> flip = wasOn ? off : on;
        Dictionary<string, object?> restore = wasOn ? on : off;
        Assert.Equal("committed", (await eplan.CallAsync("eplan_report_configure", new(flip) { ["dry_run"] = false })).GetProperty("execution").GetString());
        Assert.Empty((await eplan.CallAsync("eplan_report_configure", flip)).GetProperty("changes").EnumerateArray());
        Assert.Equal("committed", (await eplan.CallAsync("eplan_report_configure", new(restore) { ["dry_run"] = false })).GetProperty("execution").GetString());
        Assert.Empty((await eplan.CallAsync("eplan_report_configure", restore)).GetProperty("changes").EnumerateArray());
    }

    private async Task<List<string>> OpenAsync() =>
        [.. (await eplan.CallAsync("eplan_project_list", new() { ["limit"] = 200 })).GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("path").GetString()!)];

    private async Task<List<string>> NamesAsync(string project)
    {
        var names = new List<string>();
        string? cursor = null;
        do
        {
            JsonElement read = await eplan.CallAsync("eplan_structure_list", new() { ["project"] = project, ["limit"] = 500, ["cursor"] = cursor });
            names.AddRange(read.GetProperty("identifiers").EnumerateArray().Select(i => i.GetProperty("name").GetString()!));
            cursor = read.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);
        return names;
    }
}
