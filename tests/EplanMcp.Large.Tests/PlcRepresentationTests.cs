using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlcRepresentationTests(LiveEplan eplan)
{
    [Theory]
    [InlineData("1.1", "Voltage US", "1", 104)]
    [InlineData("RJ45", "ETHERNET", "X1", 40)]
    public async Task OverviewThenCircuitPoint_PreservesBothRepresentationsAndOneUndo(string point, string text, string plug, int x)
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string original = await session.StateAsync();
        string overview = session.NextPage, circuit = session.NextPage;
        var undo = new Stack<string>();
        bool known = true;
        async Task<JsonElement> Write(string tool, Dictionary<string, object?> args)
        {
            string before = await session.StateAsync();
            TestContext.Current.TestOutputHelper!.WriteLine(tool + " request: " + JsonSerializer.Serialize(args));
            bool responded = false;
            try
            {
                (bool success, JsonElement result) = await eplan.CallOrErrorAsync(tool, args);
                responded = true;
                TestContext.Current.TestOutputHelper.WriteLine(tool + " result: " + result.GetRawText());
                string? state = result.TryGetProperty("execution", out JsonElement execution) ? execution.GetString() : null;
                if (state == ExecutionState.Committed)
                { undo.Push(before); }
                else if (state is not (ExecutionState.NotStarted or ExecutionState.RolledBack))
                { known = false; session.PauseCleanup(); }
                Assert.True(success, result.GetRawText());
                Assert.Equal(ExecutionState.Committed, state);
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
            {
                ["project"] = session.Project,
                ["pages"] = new[] { new { name = overview, page_type = "Overview" }, new { name = circuit, page_type = "Circuit" } },
                ["dry_run"] = false
            });
            await Write("eplan_macro_insert", new()
            {
                ["project"] = session.Project,
                ["page"] = overview,
                ["part"] = "PXC.2861344",
                ["part_variant"] = "1",
                ["representation"] = "Overview",
                ["variant"] = 0,
                ["x"] = 0,
                ["y"] = 292,
                ["macro_box"] = false,
                ["dry_run"] = false
            });
            LiveSnapshots.Snapshot beforePoint = await session.SnapshotAsync(overview);
            string tag = Assert.Single(beforePoint.Objects("function"), f => f.Category == "PLCBox" && f.Main == true).Name!;
            string full = tag + ":" + point;
            Assert.Contains(beforePoint.Objects("function"), f => f.Name == full && f.Category is "PLCTerminal" or "BusTerminal");
            await Write("eplan_device_place", new()
            {
                ["project"] = session.Project,
                ["page"] = circuit,
                ["library"] = "IEC_symbol",
                ["symbol"] = "PLC_CBOX_PCON_PLUG",
                ["symbol_variant"] = 0,
                ["x"] = x,
                ["y"] = 92,
                ["tag"] = tag,
                ["connection_designations"] = new[] { point },
                ["plc_connection_point"] = point,
                ["plc_text"] = text,
                ["plc_plug_designation"] = plug,
                ["dry_run"] = false
            });
            LiveSnapshots.Snapshot after = await session.SnapshotAsync(overview, circuit);
            TestContext.Current.TestOutputHelper!.WriteLine("representations readback: " + JsonSerializer.Serialize(after.Objects("function")));
            Assert.Contains(after.Objects("function"), f => f.Name == full && f.Page == overview);
            Assert.Contains(after.Objects("function"), f => f.Name == full && f.Page == circuit && f.Main == false);
            await Undo();
            Assert.DoesNotContain((await session.SnapshotAsync(circuit)).Objects("function"), f => f.Name == full);
        }
        finally
        {
            if (known)
            {
                while (undo.Count > 0)
                { await Undo(); }
                Assert.Equal(original, await session.StateAsync());
            }
        }
    }
}
