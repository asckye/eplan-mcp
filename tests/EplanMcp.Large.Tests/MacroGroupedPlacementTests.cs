using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class MacroGroupedPlacementTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    [Fact]
    public async Task GroupedOverviewLinesCanFormBlockAndBeDeletedWithOneUndoEach()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        string page = session.NextPage;
        await Call("eplan_page_create", new() { ["project"] = session.Project, ["pages"] = new[] { new { name = page, page_type = "Overview" } }, ["dry_run"] = false });
        await Call("eplan_macro_insert", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["part"] = "PXC.2861344",
            ["part_variant"] = "1",
            ["representation"] = "Overview",
            ["variant"] = 0,
            ["x"] = 0,
            ["y"] = 292,
            ["macro_box"] = false,
            ["dry_run"] = false
        });
        List<JsonElement> before = await Read();
        JsonElement[] grouped = [.. before.Where(IsGroupedLine).Take(6)];
        Assert.Equal(6, grouped.Length);
        int lineCount = before.Count(p => p.GetProperty("kind").GetString() == "Line");
        string initial = await Hash();
        JsonElement created = await Call("eplan_placement_create", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["dry_run"] = false,
            ["items"] = new[] { new { kind = "block", name = "grouped_overview", members = grouped.Take(3).Select(p => p.GetProperty("locator")).ToArray() } }
        });
        Assert.Equal("committed", created.GetProperty("execution").GetString());
        List<JsonElement> afterBlock = await Read();
        JsonElement block = Assert.Single(afterBlock, p => p.GetProperty("kind").GetString() == "Block" && p.GetProperty("properties").TryGetProperty("name", out JsonElement name) && name.GetString() == "grouped_overview");
        Assert.Equal("3", block.GetProperty("properties").GetProperty("count").GetString());
        JsonElement locator = block.GetProperty("locator");
        Assert.Equal(3, afterBlock.Count(p => p.TryGetProperty("parent", out JsonElement parent) && parent.ValueKind == JsonValueKind.Object && parent.GetProperty("oid").GetInt64() == locator.GetProperty("oid").GetInt64()));
        await session.UndoAsync();
        Assert.Equal(initial, await Hash());

        JsonElement[] targets = [.. (await Read()).Where(IsGroupedLine).Skip(3).Take(3).Select(p => p.GetProperty("locator"))];
        Assert.Equal(3, targets.Length);
        host.Approvals.Approve("eplan_placement_delete", session.Project, targets.Select(l => $"delete drawing {page}:{l.GetProperty("type")}/{l.GetProperty("oid")}"));
        JsonElement deleted = await Call("eplan_placement_delete", new() { ["project"] = session.Project, ["page"] = page, ["items"] = targets, ["dry_run"] = false });
        Assert.Equal("committed", deleted.GetProperty("execution").GetString());
        Assert.Equal(lineCount - 3, (await Read()).Count(p => p.GetProperty("kind").GetString() == "Line"));
        await session.UndoAsync();
        Assert.Equal(initial, await Hash());

        async Task<string> Hash() => SnapshotCanonical.Sha256(JsonSerializer.Serialize(await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, new() { Project = session.Project, Page = page }, TestContext.Current.CancellationToken)));
        async Task<List<JsonElement>> Read()
        {
            var rows = new List<JsonElement>();
            string? cursor = null;
            do
            {
                var args = new Dictionary<string, object?> { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" };
                if (cursor is not null)
                { args["drawing_cursor"] = cursor; }
                JsonElement result = await host.CallAsync("eplan_page_get", args);
                Evidence("readback", result);
                rows.AddRange(result.GetProperty("drawings").GetProperty("items").EnumerateArray());
                cursor = result.GetProperty("drawings").GetProperty("nextCursor").GetString();
            } while (cursor is not null);
            return rows;
        }
        async Task<JsonElement> Call(string tool, Dictionary<string, object?> args)
        {
            Evidence("request", new { tool, args });
            try
            { JsonElement result = await host.CallAsync(tool, args); Evidence("result", result); return result; }
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
    private static bool IsGroupedLine(JsonElement row) => row.GetProperty("kind").GetString() == "Line"
        && row.GetProperty("properties").TryGetProperty("grouped", out JsonElement grouped) && grouped.GetString() == "true"
        && (!row.TryGetProperty("parent", out JsonElement parent) || parent.ValueKind == JsonValueKind.Null);
    private static void Evidence(string label, object value) => TestContext.Current.TestOutputHelper?.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}
