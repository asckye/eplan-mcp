using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlcPropertyB8Tests(LiveEplan eplan)
{
    [Fact]
    public async Task B8_PxcModule_PropertiesReadBackLinkedValuesAndUndoOnce()
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
            await using BridgeClient reader = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
            SnapshotObject box = Assert.Single(after.Objects("function"), f => f.Main == true && f.Category == "PLCBox" && f.Page == overview);
            SnapshotObject schematic = after.Objects("function").First(f => f.Category == "PLCTerminal" && f.Page == circuit);
            SnapshotObject overviewPoint = Assert.Single(after.Objects("function"), f => f.Category == "PLCTerminal" && f.Page == overview && f.Name == schematic.Name);
            object Locator(SnapshotObject f) => new { project = session.Project, kind = "function", type = f.Type, oid = f.Oid, name = f.Name, page = f.Page, category = f.Category, pos = f.Pos };
            async Task<List<PropertyReadRow>> Read(SnapshotObject f, params int[] ids)
            {
                var rows = await LiveProperties.ReadAsync(reader, new PropertyReadArgs
                {
                    ProjectPath = session.Project,
                    Detailed = true,
                    Language = "en_US",
                    Target = new PropertyTarget(locator: new ObjectLocator
                    { Project = session.Project, Kind = "function", Type = f.Type, Oid = f.Oid, Name = f.Name, Page = f.Page, Category = f.Category, Pos = f.Pos }),
                    Properties = [.. ids.Select(id => new PropertySelector(propertyId: id))],
                });
                TestContext.Current.TestOutputHelper!.WriteLine("property readback: " + JsonSerializer.Serialize(rows));
                return rows;
            }
            static string Value(PropertyReadRow row) => row.Value?.Text ?? row.Value?.IntegerValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            int[] boxIds = [20161, 20408, 20409, 20410, 20411, 20427, 20308];
            int[] pointIds = [20400, 20402, 20407, 20011, 20031, 20120, 20108, 20420, 20421, 20422];
            List<PropertyReadRow> boxBefore = await Read(box, boxIds);
            List<PropertyReadRow> overviewBeforeProperties = await Read(overviewPoint, pointIds);
            List<PropertyReadRow> circuitBefore = await Read(schematic, pointIds);
            LiveProperties.Describe(boxBefore.Concat(overviewBeforeProperties));
            string beforeWrite = await session.StateAsync();
            var writes = new List<object>
            {
                new { target = new { locator = Locator(overviewPoint) }, property = new { property_id = 20400 }, value = "%IX99.0" },
                new { target = new { locator = Locator(schematic) }, property = new { property_id = 20011 }, language = "en_US", value = "R6 PLC input" },
                new { target = new { locator = Locator(box) }, property = new { property_id = 20161 }, value = "R6 PLC configuration" },
                new { target = new { locator = Locator(box) }, property = new { property_id = 20408 }, value = "R6 station" },
            };
            await Write("eplan_property_set", new() { ["project"] = session.Project, ["changes"] = writes, ["dry_run"] = false });
            List<PropertyReadRow> boxAfter = await Read(box, boxIds);
            List<PropertyReadRow> overviewAfter = await Read(overviewPoint, pointIds);
            List<PropertyReadRow> circuitAfter = await Read(schematic, pointIds);
            Assert.Equal("R6 PLC configuration", Value(boxAfter.Single(r => r.PropertyId == 20161)));
            Assert.Equal("R6 station", Value(boxAfter.Single(r => r.PropertyId == 20408)));
            Assert.Equal("%IX99.0", Value(overviewAfter.Single(r => r.PropertyId == 20400)));
            Assert.Equal(Value(circuitBefore.Single(r => r.PropertyId == 20400)), Value(circuitAfter.Single(r => r.PropertyId == 20400)));
            Assert.Equal(Value(overviewBeforeProperties.Single(r => r.PropertyId == 20011)), Value(overviewAfter.Single(r => r.PropertyId == 20011)));
            foreach (int id in new[] { 20031, 20120 })
            {
                Assert.Equal("R6 PLC input", Value(overviewAfter.Single(r => r.PropertyId == id)));
            }
            Assert.Equal("R6 PLC configuration", Value(overviewAfter.Single(r => r.PropertyId == 20108)));
            Assert.Equal("R6 station", Value(overviewAfter.Single(r => r.PropertyId == 20420)));
            await Undo();
            Assert.Equal(beforeWrite, await session.StateAsync());
            Assert.Equal(boxBefore.Select(Value), (await Read(box, boxIds)).Select(Value));
            Assert.Equal(overviewBeforeProperties.Select(Value), (await Read(overviewPoint, pointIds)).Select(Value));
            Assert.Equal(circuitBefore.Select(Value), (await Read(schematic, pointIds)).Select(Value));
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
