using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Host.Tests.Tools;

public sealed partial class PlacementCreateProtocolTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(1000)]
    public async Task BlockPreviewMapsDrawingLocatorsAndSummarizesWithinBudget(int count)
    {
        await WithClient(async (client, harness, store) =>
        {
            for (int i = 1; i <= count; i++)
            { store.Geometry.Placements.Add(new() { Kind = "Line", Locator = new() { Project = harness.ProjectPath, Page = "P", Type = 4, Oid = i, Digest = new string('a', 64) } }); }
            var args = Args();
            args.Remove("dry_run");
            args["items"] = new[] { new { kind = "block", name = "144_268557", members = store.Geometry.Placements.Select(p => new { project = p.Locator.Project, page = p.Locator.Page, type = p.Locator.Type, oid = p.Locator.Oid, digest = p.Locator.Digest }).ToArray() } };
            JsonElement result = WriteToolHarness.Structured(await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token));
            Assert.Equal(ExecutionState.NotStarted, result.GetProperty("execution").GetString());
            Assert.Equal(count, result.GetProperty("items")[0].GetProperty("actual").GetProperty("count").GetInt32());
            Assert.InRange(result.GetRawText().Length, 1, 2000);
            Assert.Empty(harness.Bridge.Writes);
            Assert.Empty(store.Items);
            PlacementTextCreateArgs sent = Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlacementTextCreatePlan).Args);
            Assert.Equal(count, sent.Items![0].Members!.Count);
            Assert.Equal(harness.ProjectPath, sent.Items[0].Members![0].Project);
            Assert.Equal(4, sent.Items[0].Members![0].Type);
        });
    }
}
