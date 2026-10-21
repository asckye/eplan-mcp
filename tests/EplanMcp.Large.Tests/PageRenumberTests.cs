using System.Globalization;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_page_renumber on the write copy through <see cref="DestructiveHost"/> (docs/tools/eplan_page_renumber.md,
/// section 7): new pages renumbered as S12 did, and a start whose numbers are taken. Each test deletes its pages again,
/// so /200 to /220 are free for the next run.
/// </summary>
[Trait("Size", "Large")]
public sealed class PageRenumberTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_page_renumber";

    private readonly DestructiveHost _host;

    public PageRenumberTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task ThreeNewPages_From200InStepsOf10_Become200_210And220()
    {
        string[] renumbered = [Page(200), Page(210), Page(220)];
        string[] pages = await _host.NewPagesAsync(3);
        try
        {
            Assert.True(
                DestructiveHost.Number(pages[^1]) < 200,
                $"{DestructiveHost.Prefix} of {_host.Writable} has reached page 200, so the renumbered pages would meet its own; copy the write copies again (README.md).");
            Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(pages), ["start"] = 200, ["step"] = 10 };

            JsonElement dryRun = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
            _host.Approvals.Approve(Tool, _host.Writable, pages.Select(p => "renumber page " + p));
            JsonElement result = await _host.CallAsync(Tool, args);

            Assert.Equal(("not_started", JsonValueKind.Null, JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("changed").ValueKind, dryRun.GetProperty("backup").ValueKind));

            // S12, A20: per structure, from 200 in steps of 10, in the order given.
            Assert.Equal(("committed", "AI: eplan_page_renumber", 0), (result.GetProperty("execution").GetString(), result.GetProperty("undo").GetString(), result.GetProperty("unchanged").GetInt32()));
            Assert.Equal(
                pages.Zip(renumbered, (before, after) => (before, after)),
                result.GetProperty("changed").EnumerateArray().Select(c => (c.GetProperty("from").GetString()!, c.GetProperty("to").GetString()!)));
            string backup = result.GetProperty("backup").GetString()!;
            Assert.True(File.Exists(backup), "No backup copy at " + backup);
            foreach (string page in renumbered)
            {
                Assert.True(await _host.HasPageAsync(page), page);
            }

            foreach (string page in pages)
            {
                Assert.False(await _host.HasPageAsync(page), page);
            }

            await _host.DeleteAsync(renumbered);
        }
        catch
        {
            await _host.RemoveQuietlyAsync([.. pages, .. renumbered]);
            throw;
        }
    }

    [Fact]
    public async Task ToNumbersThePagesBeforeThemHave_IsAlreadyExists_AndTheNamesStay()
    {
        string[] pages = await _host.NewPagesAsync(2);
        try
        {
            // Start 1 would name them /1 and /2, which the sample project has: EPLAN refuses with S029167 (A20).
            Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(pages), ["start"] = 1 };
            _host.Approvals.Approve(Tool, _host.Writable, pages.Select(p => "renumber page " + p));

            JsonElement error = await _host.ErrorAsync(Tool, args);

            Assert.Equal(("ALREADY_EXISTS", "rolled_back"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            Assert.True(File.Exists(error.GetProperty("metadata").GetProperty("backup").GetString()), "The rolled back write keeps its backup: " + error);
            foreach (string page in pages.Append(Page(1)).Append(Page(2)))
            {
                Assert.True(await _host.HasPageAsync(page), page);
            }

            await _host.DeleteAsync(pages);
        }
        catch
        {
            await _host.RemoveQuietlyAsync(pages);
            throw;
        }
    }

    private static string Page(int number) => DestructiveHost.Prefix + number.ToString(CultureInfo.InvariantCulture);
}
