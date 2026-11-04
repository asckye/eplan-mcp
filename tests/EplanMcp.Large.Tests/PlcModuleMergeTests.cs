using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlcModuleMergeTests(LiveEplan eplan)
{
    [Fact]
    public async Task OverviewAndMultiLineMacroPoints_JoinExistingPlcBox_ThenOneUndoRestores()
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
            await Write("eplan_macro_insert", new()
            {
                ["project"] = session.Project,
                ["page"] = circuit,
                ["part"] = "PXC.2861344",
                ["part_variant"] = "1",
                ["representation"] = "MultiLine",
                ["variant"] = 0,
                ["numbering"] = "keep",
                ["x"] = 16,
                ["y"] = 168,
                ["macro_box"] = false,
                ["dry_run"] = false
            });
            LiveSnapshots.Snapshot before = await session.SnapshotAsync(overview, circuit);
            string target = Assert.Single(before.Objects("function"), f => f.Page == overview && f.Category == "PLCBox" && f.Main == true).Name!;
            SnapshotObject[] points = [.. before.Objects("function").Where(f => f.Page == circuit && f.Category == "PLCTerminal")];
            Assert.NotEmpty(points);
            string source = Assert.Single(points.Select(f => f.Name![..f.Name!.IndexOf(':', StringComparison.Ordinal)]).Distinct(StringComparer.Ordinal));
            Assert.NotEqual(target, source);
            Assert.All(before.Objects("function").Where(f => f.Name == source || (f.Name ?? "").StartsWith(source + ":", StringComparison.Ordinal)), f =>
            { Assert.False(f.Main); Assert.Equal("PLCTerminal", f.Category); });
            string[] overviewBefore = [.. before.Objects("function").Where(f => f.Page == overview).Select(Shape).Order(StringComparer.Ordinal)];
            JsonElement merged = await Write("eplan_device_update", new()
            { ["project"] = session.Project, ["tag"] = source, ["new_tag"] = target, ["dry_run"] = false });
            LiveSnapshots.Snapshot after = await session.SnapshotAsync(overview, circuit);
            TestContext.Current.TestOutputHelper!.WriteLine("both representations readback: " + JsonSerializer.Serialize(after.Objects("function")));
            TestContext.Current.TestOutputHelper.WriteLine("device readback: " + (await eplan.CallAsync("eplan_device_get", new() { ["project"] = session.Project, ["tag"] = target })).GetRawText());
            Assert.Equal(points.Length, merged.GetProperty("device").GetProperty("functions").GetInt32());
            Assert.Equal(target, merged.GetProperty("device").GetProperty("tag").GetString());
            Assert.Equal(overviewBefore, after.Objects("function").Where(f => f.Page == overview).Select(Shape).Order(StringComparer.Ordinal));
            Assert.Equal(points.Select(f => target + f.Name![source.Length..]).Order(StringComparer.Ordinal),
                after.Objects("function").Where(f => f.Page == circuit && f.Category == "PLCTerminal").Select(f => f.Name).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(after.Objects("function"), f => f.Name == source || (f.Name ?? "").StartsWith(source + ":", StringComparison.Ordinal));
            await Undo();
            Assert.Equal(before.Hash, (await session.SnapshotAsync(overview, circuit)).Hash);
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

    private static string Shape(SnapshotObject function) => $"{function.Name}\t{function.Category}\t{function.Main}\t{function.Pos}\t{function.Page}";
}
