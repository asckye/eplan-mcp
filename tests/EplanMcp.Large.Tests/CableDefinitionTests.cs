using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class CableDefinitionTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
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
            using JsonDocument inventory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "logs", "large-cable-inventory.json"), TestContext.Current.CancellationToken));
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
                    kind = "cable_definition", points = new double[][] { [15 * geometry.GridSize, 35 * geometry.GridSize], [55 * geometry.GridSize, 35 * geometry.GridSize] },
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
            JsonElement result = await eplan.CallAsync("eplan_connection_create", args);
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            Assert.Equal(preview.GetProperty("undoSteps").GetInt32(), result.GetProperty("undoSteps").GetInt32());
            Assert.Equal(6, result.GetProperty("undoSteps").GetInt32());
            JsonElement[] actual = result.GetProperty("actualCores").EnumerateArray().ToArray();
            Assert.Equal(wires.Select(w => w.GetProperty("Locator").GetProperty("Oid").GetInt64()), actual.Select(c => c.GetProperty("oid").GetInt64()));
            string tag = actual[0].GetProperty("designation").GetString()!;
            Assert.True(EplanMcp.Bridge.Core.Operations.CableDefinitionPlanner.SameName("=GAA+A1-W9", tag, page));
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
            for (int i = 0; i < result.GetProperty("undoSteps").GetInt32(); i++)
            { await session.UndoAsync(); }
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
            JsonElement restored = await eplan.CallAsync("eplan_connection_create", new(args) { ["dry_run"] = true });
            Assert.Equal(preview.GetProperty("fingerprint").GetString(), restored.GetProperty("fingerprint").GetString());
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }
}
