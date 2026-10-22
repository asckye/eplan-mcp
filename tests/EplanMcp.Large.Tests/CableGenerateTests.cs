using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>S34 sample regression only, on LargeWrite; this does not prove positive cable generation (tool spec 7).</summary>
[Trait("Size", "Large")]
public sealed class CableGenerateTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_cable_generate";
    private readonly DestructiveHost _host;

    public CableGenerateTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task ThreeExplicitStandardSchemes_KeepAll78CablesAndTheirNames_AndMakeAReadableBackup()
    {
        JsonElement project = await _host.CallAsync("eplan_project_get", new() { ["project"] = _host.Writable });
        int pages = project.GetProperty("counts").GetProperty("pages").GetInt32();
        // eplan_device_list gives one row per cable device (56 in the sample); the tool counts cable functions (78, S34).
        string[] before = await CableTagsAsync();
        Dictionary<string, object?> args = new()
        {
            ["project"] = _host.Writable,
            ["creation_scheme"] = "Standard",
            ["numbering_scheme"] = "Standard",
            ["selection_scheme"] = "Standard",
        };
        JsonElement planned = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        Assert.Equal(("not_started", 78), (planned.GetProperty("execution").GetString(), planned.GetProperty("before").GetProperty("cables").GetInt32()));

        _host.Approvals.Approve(Tool, _host.Writable,
            ["Generate or update automatic cables in this project using creation Standard, numbering Standard, selection Standard. Existing cable names will be preserved; existing cable renumbering is outside this operation. New cables and selection effects cannot be predicted in advance. A project backup will be made first. Undo after commit has not been verified."]);
        JsonElement result = TerminalRenumberTests.Answered(await _host.CallAsync(Tool, args));
        string[] after = await CableTagsAsync();
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.Order(StringComparer.Ordinal), after.Order(StringComparer.Ordinal));
        Assert.Equal(("committed", 78, 78, JsonValueKind.Null), (result.GetProperty("execution").GetString(), result.GetProperty("before").GetProperty("cables").GetInt32(), result.GetProperty("after").GetProperty("cables").GetInt32(), result.GetProperty("undo").ValueKind));
        JsonElement[] changes = [.. result.GetProperty("changes").EnumerateArray()];
        Assert.DoesNotContain(changes, c => c.GetProperty("kind").GetString() is "created" or "removed");
        Assert.Empty(changes);
        Assert.Equal(0, result.GetProperty("changedCount").GetInt32());
        Assert.Empty(before.Except(after, StringComparer.Ordinal));
        Assert.False(result.GetProperty("truncated").GetBoolean());
        TestContext.Current.TestOutputHelper?.WriteLine("Observed business changes: " + result.GetProperty("changes"));

        string backup = result.GetProperty("backup").GetString()!;
        Assert.True(File.Exists(backup) && Directory.Exists(Path.ChangeExtension(backup, ".edb")), "No project backup at " + backup);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(_host.ProcessId);
        ProjectSnapshotData copy = await bridge.InvokeAsync<ProjectSnapshotArgs, ProjectSnapshotData>(OperationNames.ProjectSnapshot,
            new ProjectSnapshotArgs { Project = backup, OpenReadOnly = true, TimeLimitSeconds = 1 }, TestContext.Current.CancellationToken);
        Assert.Equal(pages, copy.PageCount);
    }

    private async Task<string[]> CableTagsAsync()
    {
        JsonElement listed = await _host.CallAsync("eplan_device_list", new() { ["project"] = _host.Writable, ["category"] = "Cable", ["limit"] = 200 });
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("nextCursor").ValueKind);
        return [.. listed.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!)];
    }
}
