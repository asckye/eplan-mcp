using System.Text.Json;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class EmptyMacroBlockTests(LiveEplan eplan)
{
    [Fact]
    public async Task Pxc2861289OverviewWithEmptyBlocksInsertsAndReadsBack()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = session.NextPage;
        await Call("eplan_page_create", new() { ["project"] = session.Project, ["pages"] = new[] { new { name = page, page_type = "Overview" } }, ["dry_run"] = false });
        string before = await session.StateAsync();
        JsonElement result = await Call("eplan_macro_insert", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["part"] = "PXC.2861289",
            ["part_variant"] = "1",
            ["representation"] = "Overview",
            ["variant"] = 0,
            ["x"] = 0,
            ["y"] = 292,
            ["macro_box"] = false,
            ["dry_run"] = false
        });
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.True(result.GetProperty("objectCount").GetInt32() > 0);
        var rows = await Read();
        Assert.Contains(rows, row => row.GetProperty("kind").GetString() == "Block");
        Assert.Contains(rows, row => row.GetProperty("kind").GetString() == "PLC");
        await session.UndoAsync();
        Assert.Equal(before, await session.StateAsync());

        async Task<List<JsonElement>> Read()
        {
            var rows = new List<JsonElement>();
            string? cursor = null;
            do
            {
                var request = new Dictionary<string, object?> { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" };
                if (cursor is not null)
                { request["drawing_cursor"] = cursor; }
                JsonElement readback = await eplan.CallAsync("eplan_page_get", request);
                Evidence("readback", readback);
                rows.AddRange(readback.GetProperty("drawings").GetProperty("items").EnumerateArray());
                cursor = readback.GetProperty("drawings").GetProperty("nextCursor").GetString();
            } while (cursor is not null);
            return rows;
        }
        async Task<JsonElement> Call(string tool, Dictionary<string, object?> request)
        {
            Evidence("request", new { tool, request });
            try
            {
                (bool success, JsonElement result) = await eplan.CallOrErrorAsync(tool, request);
                Evidence("result", result);
                Assert.True(success, result.GetRawText());
                return result;
            }
            catch (Exception error)
            {
                Evidence("failed result", error.ToString());
                try
                { await Read(); }
                catch (Exception readError) { Evidence("failed readback", readError.ToString()); }
                throw;
            }
        }
    }
    private static void Evidence(string label, object value) => TestContext.Current.TestOutputHelper?.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}
