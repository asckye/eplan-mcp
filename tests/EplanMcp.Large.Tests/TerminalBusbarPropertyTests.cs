using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class TerminalBusbarPropertyTests(LiveEplan eplan)
{
    [Fact]
    public async Task TerminalPositionAndBusbarText_ReadBackAndEachUndoRestoresSnapshot()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal("LargeWrite", Path.GetFileName(session.Project));
        string page = session.NextPage;
        var undo = new Stack<string>();
        bool known = true;
        async Task<JsonElement> Write(string tool, Dictionary<string, object?> args)
        {
            Assert.False((bool)args["dry_run"]!);
            string before = await session.StateAsync();
            TestContext.Current.TestOutputHelper!.WriteLine(tool + " request: " + JsonSerializer.Serialize(args));
            bool responded = false;
            try
            {
                (bool success, JsonElement result) = await eplan.CallOrErrorAsync(tool, args);
                responded = true;
                TestContext.Current.TestOutputHelper.WriteLine(tool + " result: " + result.GetRawText());
                string? execution = result.TryGetProperty("execution", out JsonElement state) ? state.GetString() : null;
                if (execution == ExecutionState.Committed)
                { undo.Push(before); }
                else if (execution is not (ExecutionState.NotStarted or ExecutionState.RolledBack))
                { known = false; session.PauseCleanup(); }
                Assert.True(success, result.GetRawText());
                Assert.Equal(ExecutionState.Committed, execution);
                return result;
            }
            catch
            {
                if (!responded)
                { known = false; session.PauseCleanup(); }
                TestContext.Current.TestOutputHelper.WriteLine("failure readback: " + JsonSerializer.Serialize(await session.SnapshotAsync()));
                throw;
            }
        }
        async Task Undo()
        {
            try
            { await session.UndoAsync(); Assert.Equal(undo.Peek(), await session.StateAsync()); undo.Pop(); }
            catch { known = false; session.PauseCleanup(); throw; }
        }
        try
        {
            await Write("eplan_page_create", new()
            { ["project"] = session.Project, ["pages"] = new[] { new { name = page, page_type = "Circuit" } }, ["dry_run"] = false });
            await Write("eplan_device_place", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["terminal_strip"] = "+A1-XR9" + session.NextNumber,
                ["terminal_number"] = "1",
                ["x"] = 40,
                ["y"] = 120,
                ["dry_run"] = false
            });
            string tag = "+A1-WER9" + session.NextNumber;
            foreach ((string symbol, int x) in new[] { ("BBDEF", 80), ("BCP", 120) })
            {
                await Write("eplan_device_place", new()
                {
                    ["project"] = session.Project,
                    ["page"] = page,
                    ["library"] = "SPECIAL",
                    ["symbol"] = symbol,
                    ["symbol_variant"] = 0,
                    ["tag"] = tag,
                    ["x"] = x,
                    ["y"] = 120,
                    ["dry_run"] = false
                });
            }
            LiveSnapshots.Snapshot created = await session.SnapshotAsync(page);
            foreach ((string category, int id, string value) in new[] { ("Terminal", 20367, "7"), ("Busbar", 20011, "PE") })
            {
                SnapshotObject target = Assert.Single(created.Objects("function"), f => f.Category == category && f.Main == false);
                var locator = new ObjectLocator
                { Project = session.Project, Kind = "function", Type = target.Type, Oid = target.Oid, Name = target.Name, Page = target.Page, Category = target.Category, Pos = target.Pos };
                var change = new Dictionary<string, object?>
                { ["target"] = new { locator }, ["property"] = new { property_id = id }, ["value"] = value };
                if (id == 20011)
                { change["language"] = "en_US"; }
                await Write("eplan_property_set", new()
                { ["project"] = session.Project, ["changes"] = new[] { change }, ["dry_run"] = false });
                try
                {
                    JsonElement read = await eplan.CallAsync("eplan_property_get", new()
                    {
                        ["project"] = session.Project,
                        ["queries"] = new[] { new { target = new { locator }, properties = new[] { new { property_id = id } } } },
                        ["language"] = "en_US",
                        ["response_format"] = "detailed"
                    });
                    TestContext.Current.TestOutputHelper!.WriteLine("property readback: " + read.GetRawText());
                    JsonElement row = Assert.Single(read.GetProperty("items").EnumerateArray());
                    Assert.Equal("value", row.GetProperty("status").GetString());
                    Assert.Equal(value, row.GetProperty("value").GetString());
                }
                catch
                {
                    TestContext.Current.TestOutputHelper!.WriteLine("failure readback: " + JsonSerializer.Serialize(await session.SnapshotAsync()));
                    throw;
                }
                await Undo();
            }
        }
        finally
        {
            if (known)
            { while (undo.Count > 0) { await Undo(); } }
        }
    }
}
