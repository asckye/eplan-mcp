using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_session_close and eplan_session_restart on the live EPLAN (ADR-0058; large-pending.md). It may restart the
/// EPLAN every other class uses, so run it alone and last: run-large -Classes SessionLifecycleTests. With a project
/// outside the scratch root open (the slot's LargeOutside copy), closing is refused by the project's name, without its
/// path, and EPLAN keeps running. With only scratch projects open, the restart closes EPLAN as install-addin does (A23),
/// with projects open, starts the same variant and waits for the add-in; the projects are then opened again.
/// </summary>
[Trait("Size", "Large")]
public sealed class SessionLifecycleTests(LiveEplan eplan)
{
    [Fact]
    public async Task Closing_IsRefusedForAProjectOutsideScratch_OrRestartsWhenAllAreScratch()
    {
        int before = eplan.ProcessId;
        JsonElement projects = await CallAsync("eplan_project_list", []);
        string[] open = [.. projects.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("path").GetString()!)];
        string scratch = LiveEplan.ScratchRoot();
        string[] outside = [.. open.Where(p => !Host.Safety.ScratchPaths.IsInside(p, [scratch]))];

        if (outside.Length > 0)
        {
            (bool closed, JsonElement refusal) = await eplan.CallOrErrorAsync("eplan_session_close", new() { ["process_id"] = before, ["dry_run"] = false });
            TestContext.Current.TestOutputHelper!.WriteLine("eplan_session_close result: " + refusal.GetRawText());
            Assert.False(closed);
            Assert.Equal(ErrorReason.PolicyDenied, refusal.GetProperty("reason").GetString());
            string message = refusal.GetProperty("message").GetString()!;
            Assert.All(outside, p => Assert.Contains(Path.GetFileName(p), message, StringComparison.Ordinal));
            Assert.All(outside, p => Assert.DoesNotContain(p, message, StringComparison.OrdinalIgnoreCase));
            JsonElement status = await CallAsync("eplan_session_status", []);
            Assert.Equal(before, status.GetProperty("eplan").GetProperty("processId").GetInt32());
            return;
        }

        JsonElement restarted = await CallAsync("eplan_session_restart", new() { ["process_id"] = before, ["dry_run"] = false });
        Assert.Equal(ExecutionState.Committed, restarted.GetProperty("close").GetProperty("execution").GetString());
        int after = restarted.GetProperty("start").GetProperty("processId").GetInt32();
        Assert.NotEqual(before, after);
        Assert.Equal(restarted.GetProperty("bridgeVersionBefore").GetString(), restarted.GetProperty("start").GetProperty("bridgeVersion").GetString());
        await CallAsync("eplan_session_select", new() { ["process_id"] = after });
        foreach (string path in open)
        { await CallAsync("eplan_project_open", new() { ["path"] = path + ".elk", ["dry_run"] = false }); }
    }

    private async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?> args)
    {
        TestContext.Current.TestOutputHelper!.WriteLine(tool + " request: " + JsonSerializer.Serialize(args));
        (bool succeeded, JsonElement result) = await eplan.CallOrErrorAsync(tool, args);
        TestContext.Current.TestOutputHelper.WriteLine(tool + " result: " + result.GetRawText());
        Assert.True(succeeded, tool + " failed: " + result.GetRawText());
        return result;
    }
}
