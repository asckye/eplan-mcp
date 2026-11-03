using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class CableDefinitionTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task GeneratedWires_DetailedListLocators_PlanCableWithoutWriting()
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build the connection helper before running cable Large tests.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            await PlanStepLiveSession.ActionAsync("EplanMcpLargeConnectionSetup /PAGE:\"" + page + "\" /DIRECTIONS:Cable");
            await eplan.CallAsync("eplan_connection_generate", new() { ["project"] = session.Project, ["pages"] = new[] { page } });
            string before = (await session.SnapshotAsync()).Hash;
            JsonElement listed = await eplan.CallAsync("eplan_connection_list", new()
            { ["project"] = session.Project, ["page"] = page, ["response_format"] = "detailed" });
            JsonElement[] wires = listed.GetProperty("connections").EnumerateArray().ToArray();
            Assert.Equal(3, wires.Length);
            Assert.False(listed.GetProperty("truncated").GetBoolean());
            await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
            PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
                new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken);
            string[] colours = ["BK", "BU", "GNYE"];
            JsonElement result = await eplan.CallAsync("eplan_connection_create", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["dry_run"] = true,
                ["response_format"] = "detailed",
                ["items"] = JsonSerializer.SerializeToElement(new[] { new
                {
                    // EPLAN right-aligns the designation and type texts at the left end; from x = 60 they crossed the
                    // 10 mm margin (R1, live 2026-11-01), so the line starts where the layout guard suggested.
                    kind = "cable_definition", points = new double[][] { [16 * geometry.GridSize, 35 * geometry.GridSize], [55 * geometry.GridSize, 35 * geometry.GridSize] },
                    designation = "-W9", type_text = "ÖLFLEX CLASSIC 100 300/500V", core_count = 3, cross_section_mm2 = 0.5,
                    cores = wires.Select((w, i) => new { connection = new { locator = w.GetProperty("locator") },
                        label = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), colour = colours[i] }).ToArray(),
                } }),
            });
            Assert.Equal(ExecutionState.NotStarted, result.GetProperty("execution").GetString());
            Assert.True(result.GetProperty("dryRun").GetBoolean());
            Assert.Equal(wires.Select(w => w.GetProperty("locator").GetRawText()),
                result.GetProperty("coreImpact").EnumerateArray().Select(c => c.GetProperty("connection").GetProperty("locator").GetRawText()));
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }

    [Fact]
    [Trait("Difference", "D4")]
    public async Task ThreeVerticalWires_DefineW9_ReadNativeCores_UndosRestore()
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build the connection helper before running T8b Large tests.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            await PlanStepLiveSession.ActionAsync("EplanMcpLargeConnectionSetup /PAGE:\"" + page + "\" /DIRECTIONS:Cable");
            await PlanStepLiveSession.ActionAsync("EplanMcpLargeConnectionSetup /PAGE:\"" + page + "\" /DIRECTIONS:CableRead");
            using JsonDocument inventory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(LiveEplan.DataRoot(), "logs", "large-cable-inventory.json"), TestContext.Current.CancellationToken));
            Assert.Equal(page, inventory.RootElement.GetProperty("page").GetString());
            JsonElement[] wires = inventory.RootElement.GetProperty("wires").EnumerateArray().ToArray();
            Assert.Equal(3, wires.Length);
            string before = (await session.SnapshotAsync()).Hash;
            await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
            PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
                new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken);
            string[] colours = ["BK", "BU", "GNYE"];
            var args = new Dictionary<string, object?>
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["response_format"] = "detailed",
                ["items"] = JsonSerializer.SerializeToElement(new[] { new
                {
                    // EPLAN right-aligns the designation and type texts at the left end; from x = 60 they crossed the
                    // 10 mm margin (R1, live 2026-11-01), so the line starts where the layout guard suggested.
                    kind = "cable_definition", points = new double[][] { [16 * geometry.GridSize, 35 * geometry.GridSize], [55 * geometry.GridSize, 35 * geometry.GridSize] },
                    designation = "-W9", type_text = "ÖLFLEX CLASSIC 100 300/500V", core_count = 3, cross_section_mm2 = 0.5,
                    cores = wires.Select((w, i) => new { connection = new { locator = new
                    {
                        project = w.GetProperty("Locator").GetProperty("Project").GetString(), page,
                        type = w.GetProperty("Locator").GetProperty("Type").GetInt32(), oid = w.GetProperty("Locator").GetProperty("Oid").GetInt64(), digest = w.GetProperty("Locator").GetProperty("Digest").GetString(),
                    } }, label = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), colour = colours[i] }).ToArray(),
                } }),
            };
            JsonElement preview = await eplan.CallAsync("eplan_connection_create", new(args) { ["dry_run"] = true });
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
            JsonElement result = await eplan.CallAsync("eplan_connection_create", new(args) { ["dry_run"] = false });
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            Assert.Equal(preview.GetProperty("undoSteps").GetInt32(), result.GetProperty("undoSteps").GetInt32());
            Assert.Equal(6, result.GetProperty("undoSteps").GetInt32());
            JsonElement[] actual = result.GetProperty("actualCores").EnumerateArray().ToArray();
            Assert.Equal(wires.Select(w => w.GetProperty("Locator").GetProperty("Oid").GetInt64()), actual.Select(c => c.GetProperty("oid").GetInt64()));
            string tag = actual[0].GetProperty("designation").GetString()!;
            Assert.True(EplanMcp.Bridge.Core.Operations.CableDefinitionPlanner.SameName("=GAA+A1-W9", tag, page), "actual cable designation: " + tag);
            JsonElement read = await eplan.CallAsync("eplan_cable_get", new() { ["project"] = session.Project, ["cable"] = tag });
            Assert.Equal("ÖLFLEX CLASSIC 100 300/500V", read.GetProperty("cable").GetProperty("type").GetString());
            Assert.Equal("3", read.GetProperty("cable").GetProperty("wireCount").GetString());
            Assert.Equal("0.5", read.GetProperty("cable").GetProperty("crossSection").GetString()!.Replace(',', '.'));
            JsonElement[] cores = read.GetProperty("cores").EnumerateArray().OrderBy(c => c.GetProperty("designation").GetString(), StringComparer.Ordinal).ToArray();
            Assert.Equal(["1", "2", "3"], cores.Select(c => c.GetProperty("designation").GetString()));
            Assert.Equal(colours, cores.Select(c => c.GetProperty("colour").GetString()));
            JsonElement existing = await eplan.CallAsync("eplan_connection_list", new() { ["project"] = session.Project, ["page"] = page });
            foreach (JsonElement core in cores)
            { Assert.Contains(existing.GetProperty("connections").EnumerateArray(), c => c.GetProperty("from").GetRawText() == core.GetProperty("from").GetRawText() && c.GetProperty("to").GetRawText() == core.GetProperty("to").GetRawText()); }
            async Task<PageDrawingGeometry> Drawings() => await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
                new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken);
            PageDrawingGeometry cableBefore = await Drawings();
            DrawingPlacement parent = Assert.Single(cableBefore.Placements, p => p.Kind == "Cable");
            DrawingPlacement[] labels = cableBefore.Placements.Where(p => p.Parent?.Oid == parent.Locator.Oid && p.Kind == "PropertyPlacement" && !string.IsNullOrWhiteSpace(p.Properties.GetValueOrDefault("display"))).ToArray();
            Assert.NotEmpty(labels);
            TestContext.Current.TestOutputHelper!.WriteLine("D4 EPLAN cable text before: " + JsonSerializer.Serialize(labels));
            var moves = labels.Select((p, i) => new
            {
                parent = parent.Locator,
                property_id = int.Parse(p.Properties["property_id"], System.Globalization.CultureInfo.InvariantCulture),
                property_index = int.Parse(p.Properties["property_index"], System.Globalization.CultureInfo.InvariantCulture),
                x_mm = 60 + i * 8,
                y_mm = 160,
                angle_deg = 90
            }).ToArray();
            var update = new Dictionary<string, object?> { ["project"] = session.Project, ["page"] = page, ["property_moves"] = moves, ["snap_to_grid"] = false, ["response_format"] = "detailed" };
            JsonElement planned = await eplan.CallAsync("eplan_placement_update", new(update) { ["dry_run"] = true });
            Assert.Equal(JsonSerializer.Serialize(cableBefore), JsonSerializer.Serialize(await Drawings()));
            JsonElement changed = await eplan.CallAsync("eplan_placement_update", new(update) { ["dry_run"] = false });
            TestContext.Current.TestOutputHelper.WriteLine("D4 approved/readback: " + changed);
            Assert.Equal(ExecutionState.Committed, changed.GetProperty("execution").GetString());
            PageDrawingGeometry after = await Drawings();
            TestContext.Current.TestOutputHelper.WriteLine("D4 EPLAN cable text after: " + JsonSerializer.Serialize(after.Placements.Where(p => p.Parent?.Oid == parent.Locator.Oid)));
            foreach (var move in moves)
            {
                DrawingPlacement text = Assert.Single(after.Placements, p => p.Parent?.Oid == parent.Locator.Oid && p.Properties.GetValueOrDefault("property_index") == move.property_index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal([move.x_mm, (double)move.y_mm], text.Location);
                Assert.Equal("90", text.Properties["angle_deg"]);
            }
            Assert.Equal(JsonSerializer.Serialize(parent), JsonSerializer.Serialize(Assert.Single(after.Placements, p => p.Parent is null && p.Locator.Oid == parent.Locator.Oid)));
            Assert.Equal(ExecutionState.NotStarted, (await eplan.CallAsync("eplan_placement_update", new(update) { ["dry_run"] = false })).GetProperty("execution").GetString());
            TestContext.Current.TestOutputHelper.WriteLine("D4 PNG: " + await eplan.CallAsync("eplan_export_image", new() { ["project"] = session.Project, ["pages"] = new[] { page } }));
            await session.UndoAsync();
            Assert.Equal(JsonSerializer.Serialize(cableBefore), JsonSerializer.Serialize(await Drawings()));
            for (int i = 0; i < result.GetProperty("undoSteps").GetInt32(); i++)
            { await session.UndoAsync(); }
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
            JsonElement restored = await eplan.CallAsync("eplan_connection_create", new(args) { ["dry_run"] = true });
            Assert.Equal(preview.GetProperty("fingerprint").GetString(), restored.GetProperty("fingerprint").GetString());
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }
}
