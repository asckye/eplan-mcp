using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class InterruptionVisibleNameTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Theory]
    [InlineData("=GAA-2L1", "-2L1")]
    [InlineData("=GAA-2Z3", "-2Z3")]
    public async Task FullInterruptionName_HasVisibleName_OneUndoRestores(string name, string shown)
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        string before = (await session.SnapshotAsync()).Hash;
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper));
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        var request = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["dry_run"] = false,
            ["generate"] = false,
            ["items"] = new[] { new { kind = "interruption_point", name, symbol = "BP", symbol_variant = 0, direction = "Right", position = new[] { 100, 100 } } },
        };
        try
        {
            TestContext.Current.TestOutputHelper!.WriteLine("request: " + JsonSerializer.Serialize(request));
            (bool ok, JsonElement result) = await eplan.CallOrErrorAsync("eplan_connection_create", request);
            TestContext.Current.TestOutputHelper.WriteLine("result: " + result);
            JsonElement observation = await ObserveAsync();
            Assert.True(ok, result.GetRawText());
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            Assert.Equal(1, result.GetProperty("undoSteps").GetInt32());
            JsonElement point = Assert.Single(observation.GetProperty("interruptions").EnumerateArray());
            Assert.Equal(name, point.GetProperty("name").GetString());
            Assert.Equal(shown, point.GetProperty("visibleName").GetString());
            await session.UndoAsync();
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
        }
        catch
        {
            try
            { await ObserveAsync(); }
            catch (Exception error) { TestContext.Current.TestOutputHelper!.WriteLine("readback failed: " + error); }
            throw;
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }

        async Task<JsonElement> ObserveAsync()
        {
            string file = Path.Combine(host.Home, "visible-name-" + Guid.NewGuid().ToString("N") + ".json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { project = session.Project, pages = new[] { page }, mode = "read" }), TestContext.Current.CancellationToken);
            await PlanStepLiveSession.ActionAsync("EplanMcpLargeConnectionSpike /REQUEST:\"" + file + "\"");
            string json = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
            TestContext.Current.TestOutputHelper!.WriteLine("readback: " + json);
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean(), json);
            return document.RootElement.GetProperty("observation").Clone();
        }
    }
}
