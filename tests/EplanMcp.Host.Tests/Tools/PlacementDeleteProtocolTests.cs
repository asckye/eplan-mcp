using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlacementDeleteProtocolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultPreview_ExplicitExecution_AndApprovalDenial(bool denied)
    {
        var channel = new FakeApprovalChannel("test", denied ? ApprovalDecision.Denied : ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny, channel);
        harness.Bridge.Handlers[OperationNames.PlacementDeletePlan] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "delete", Kind = "placement", Name = "page:4/1" }]);
            plan.Details = new() { ["preview"] = "{\"project\":\"Line1\",\"page\":\"page\",\"fingerprint\":\"f1\",\"items\":[{\"type\":4,\"oid\":1}]}" };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.PlacementDeleteExecute] = _ => JsonNode.Parse("{\"project\":\"Line1\",\"page\":\"page\",\"fingerprint\":\"f1\",\"items\":[{\"type\":4,\"oid\":1}]}")!.AsObject();
        await PreviewProtocolHarness.RunAsync(harness, async client =>
        {
            Dictionary<string, object?> args = new() { ["project"] = "Line1", ["page"] = "page", ["items"] = new[] { new { project = harness.ProjectPath, page = "page", type = 4, oid = 1, digest = new string('a', 64) } } };
            JsonElement omitted = WriteToolHarness.Structured(await client.CallToolAsync("eplan_placement_delete", args, cancellationToken: TestContext.Current.CancellationToken));
            args["dry_run"] = true;
            JsonElement explicitPreview = WriteToolHarness.Structured(await client.CallToolAsync("eplan_placement_delete", args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(JsonElement.DeepEquals(omitted, explicitPreview));
            Assert.Equal("not_started", omitted.GetProperty("execution").GetString());
            Assert.Empty(harness.Bridge.Writes);
            Assert.Empty(channel.Questions);
            Assert.Contains("dry_run: false", omitted.GetProperty("hint").GetString(), StringComparison.Ordinal);
            args["dry_run"] = false;
            var response = await client.CallToolAsync("eplan_placement_delete", args, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement result = denied ? WriteToolHarness.Error(response) : WriteToolHarness.Structured(response);
            Assert.Equal(denied ? "not_started" : "committed", result.GetProperty("execution").GetString());
            Assert.Single(channel.Questions);
            if (denied)
            { Assert.Empty(harness.Bridge.Writes); }
            else
            {
                Assert.Single(harness.Bridge.Writes);
                Assert.NotNull(result.GetProperty("backup").GetString());
            }
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task SchemaRefusesInvalidBatchCountsBeforeBridge(int count)
    {
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny);
        await PreviewProtocolHarness.RunAsync(harness, async client =>
        {
            var items = Enumerable.Range(1, count).Select(i => new { project = harness.ProjectPath, page = "page", type = 4, oid = i, digest = new string('a', 64) }).ToArray();
            var response = await client.CallToolAsync("eplan_placement_delete", new Dictionary<string, object?> { ["project"] = "Line1", ["page"] = "page", ["items"] = items }, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement error = WriteToolHarness.Error(response);
            Assert.Equal("INVALID_ARGUMENT", error.GetProperty("reason").GetString());
            Assert.Equal("not_started", error.GetProperty("execution").GetString());
            Assert.Empty(harness.Bridge.Calls);
        });
    }

    [Theory]
    [InlineData(" ", 4)]
    [InlineData("Line1", 0)]
    public async Task InvalidIdentityIsRejectedBeforeBridge(string project, int type)
    {
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny);
        await PreviewProtocolHarness.RunAsync(harness, async client =>
        {
            var args = new Dictionary<string, object?> { ["project"] = project, ["page"] = "page", ["items"] = new[] { new { project = "Line1", page = "page", type, oid = 1, digest = new string('a', 64) } } };
            JsonElement error = WriteToolHarness.Error(await client.CallToolAsync("eplan_placement_delete", args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("INVALID_ARGUMENT", error.GetProperty("reason").GetString());
            Assert.Empty(harness.Bridge.Calls);
        });
    }
}
