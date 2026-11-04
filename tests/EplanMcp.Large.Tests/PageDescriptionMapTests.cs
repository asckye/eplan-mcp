using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PageDescriptionMapTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThreeLanguages_CreateOrUpdate_ReadEachBackAndUndo(bool update)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        string page = update ? Assert.Single(await selection.CreatePagesAsync(1)) : selection.NextPage;
        var texts = new Dictionary<string, string> { ["en_US"] = "Panel", ["de_DE"] = "Schrank", ["zh_CN"] = "面板" };
        string tool = update ? "eplan_page_update" : "eplan_page_create";
        var request = new Dictionary<string, object?>
        {
            ["project"] = host.Writable,
            ["pages"] = new[] { new { name = page, description = texts } },
            ["dry_run"] = false,
        };
        string before = (await selection.SnapshotAsync()).Hash;
        TestContext.Current.TestOutputHelper!.WriteLine("request: " + JsonSerializer.Serialize(request));
        try
        {
            JsonElement result = await host.CallAsync(tool, request);
            TestContext.Current.TestOutputHelper!.WriteLine("result: " + result.GetRawText());
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            foreach ((string language, string text) in texts)
            {
                JsonElement readback = await Read(page, language);
                TestContext.Current.TestOutputHelper!.WriteLine("readback: " + readback.GetRawText());
                Assert.Equal(text, readback.GetProperty("page").GetProperty("description").GetString());
            }
            await selection.UndoAsync();
            Assert.Equal(before, (await selection.SnapshotAsync()).Hash);
        }
        catch
        {
            foreach (string language in texts.Keys)
            {
                try
                { TestContext.Current.TestOutputHelper!.WriteLine("failure readback: " + (await Read(page, language)).GetRawText()); }
                catch (Exception ex) { TestContext.Current.TestOutputHelper!.WriteLine("failure readback unavailable: " + ex.Message); }
            }
            throw;
        }
    }

    private Task<JsonElement> Read(string page, string language) => host.CallAsync("eplan_page_get", new()
    { ["project"] = host.Writable, ["page"] = page, ["language"] = language, ["response_format"] = "detailed" });
}
