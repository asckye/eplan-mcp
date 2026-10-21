using System.Text.Json;
using EplanMcp.Host.Safety;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_device_renumber on the write copy through <see cref="DestructiveHost"/> (docs/tools/eplan_device_renumber.md,
/// section 7), on a new page with the sample macro inserted by <c>number</c>: the case that ended EPLAN before only tags
/// with '?' went to PostNumerate (api-notes A20). Each test deletes its page again.
/// </summary>
[Trait("Size", "Large")]
public sealed class DeviceRenumberTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_device_renumber";

    private readonly DestructiveHost _host;

    public DeviceRenumberTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task OnlyUnnumbered_AfterAMacroInsertedWithNumber_KeepsEplanRunning_AndNumbersNothing()
    {
        string page = (await _host.NewPagesAsync(1))[0];
        try
        {
            await _host.InsertMacroAsync(page);
            Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { page }) };

            JsonElement dryRun = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
            JsonElement scope = dryRun.GetProperty("scope");
            int devices = scope.GetProperty("devices").GetInt32();
            Assert.Equal(("not_started", 1, true, JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), scope.GetProperty("pageCount").GetInt32(), scope.GetProperty("onlyUnnumbered").GetBoolean(), dryRun.GetProperty("changed").ValueKind));
            Assert.InRange(devices, 1, 50);

            _host.Approvals.Approve(Tool, _host.Writable, [Line(devices, null)]);
            JsonElement numbered = await _host.CallAsync(Tool, args);
            ApprovalQuestion question = _host.Approvals.Questions[^1];
            string[] planned = question.Plan.Details!["devices"].Split('\n');

            // A20: only the tags with '?' went to PostNumerate, and EPLAN is still there to answer.
            JsonElement status = await _host.CallAsync("eplan_session_status", []);
            Assert.Equal((true, _host.ProcessId), (status.GetProperty("connected").GetBoolean(), status.GetProperty("eplan").GetProperty("processId").GetInt32()));

            // A14, A18, A20: the contact without its coil keeps its '?', and EPLAN changes no tag at all, which leaves no
            // undo step: not_started (ADR-0010 section 1); the backup was made all the same.
            Assert.Contains(planned, tag => tag.StartsWith("=GAA+A1-?QA", StringComparison.Ordinal));
            Assert.Equal(planned.Order(StringComparer.Ordinal), numbered.GetProperty("stillUnnumbered").EnumerateArray().Select(t => t.GetString()!).Order(StringComparer.Ordinal));
            Assert.Equal(("not_started", 0, JsonValueKind.Null), (numbered.GetProperty("execution").GetString(), numbered.GetProperty("changedCount").GetInt32(), numbered.GetProperty("undo").ValueKind));
            Assert.Empty(numbered.GetProperty("changed").EnumerateArray());
            string backup = numbered.GetProperty("backup").GetString()!;
            Assert.True(File.Exists(backup), "No backup copy at " + backup);
            Assert.Contains("still contain", numbered.GetProperty("hint").GetString(), StringComparison.Ordinal);

            await _host.DeleteAsync([page]);
        }
        catch
        {
            await _host.RemoveQuietlyAsync([page]);
            throw;
        }
    }

    [Fact]
    public async Task TheSchemeDefault_IsInvalidArgumentAndRolledBack_WithS12sSchemesAsCandidates()
    {
        string page = (await _host.NewPagesAsync(1))[0];
        try
        {
            await _host.InsertMacroAsync(page);
            Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { page }), ["scheme"] = "Default" };
            int devices = (await _host.CallAsync(Tool, new(args) { ["dry_run"] = true })).GetProperty("scope").GetProperty("devices").GetInt32();
            _host.Approvals.Approve(Tool, _host.Writable, [Line(devices, "Default")]);

            JsonElement error = await _host.ErrorAsync(Tool, args);

            // No settings node for the schemes was found, so EPLAN checks the name only when it numbers (tool spec 5):
            // after the backup, inside the transaction. Its InvalidScheme message names the schemes (S12, A18).
            Assert.Equal(("INVALID_ARGUMENT", "rolled_back"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            JsonElement metadata = error.GetProperty("metadata");
            Assert.Equal(
                ["Kennbuchstabe + Zähler Fluidtechnik", "Kennbuchstabe + Zähler/Zubehör Fluidtechnik", "Kennbuchstabe Zähler", "Media_code", "Standard"],
                metadata.GetProperty("candidates").GetString()!.Split('\n').Order(StringComparer.Ordinal));
            Assert.True(File.Exists(metadata.GetProperty("backup").GetString()), "The rolled back write keeps its backup: " + metadata);

            // The rollback left the tags as they were: the next plan has the same devices to number.
            JsonElement again = await _host.CallAsync(Tool, new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { page }), ["dry_run"] = true });
            Assert.Equal(devices, again.GetProperty("scope").GetProperty("devices").GetInt32());

            await _host.DeleteAsync([page]);
        }
        catch
        {
            await _host.RemoveQuietlyAsync([page]);
            throw;
        }
    }

    /// <summary>The change line the approval shows for numbering the '?' tags of one page (DeviceRenumberRequest.Describe).</summary>
    private static string Line(int devices, string? scheme) =>
        $"renumber device tags containing '?' on 1 page ({devices} {(devices == 1 ? "device" : "devices")}), scheme: {scheme ?? "EPLAN default"}, start 1, step 1";
}
