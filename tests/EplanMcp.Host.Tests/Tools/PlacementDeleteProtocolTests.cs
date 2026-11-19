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
    private static readonly string[] _stripTags = ["-X1"];
    private static readonly string[] _overlappingTags = ["-X1", "-X1:1"];
    private static readonly string[] _conflictingPaths = ["/page", "/items", "/unplaced/0", "/unplaced/1"];

    [Fact]
    public async Task UnplacedPreviewAndApprovedExecutionUseThePlannedProjectPath()
    {
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny, channel);
        const string Preview = """{"project":"Line1","unplaced":["-X1"],"items":[{"type":4,"oid":1,"name":"-X1","category":"TerminalDefText"}],"dependents":1,"fingerprint":"f1"}""";
        harness.Bridge.Handlers[OperationNames.UnplacedDeletePlan] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "delete", Kind = "function", Name = "-X1 (TerminalDefText, unplaced)" }]);
            plan.Details = new() { ["preview"] = Preview };
            return plan;
        };
        harness.Bridge.Handlers[OperationNames.UnplacedDeleteExecute] = _ => JsonNode.Parse(Preview)!.AsObject();
        await PreviewProtocolHarness.RunAsync(harness, async client =>
        {
            var args = new Dictionary<string, object?> { ["project"] = "Line1", ["unplaced"] = _stripTags };
            JsonElement preview = WriteToolHarness.Structured(await client.CallToolAsync("eplan_placement_delete", args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(ExecutionState.NotStarted, preview.GetProperty("execution").GetString());
            Assert.Equal(1, preview.GetProperty("dependents").GetInt32());
            Assert.Empty(harness.Bridge.Writes);
            Assert.Empty(channel.Questions);
            args["dry_run"] = false;
            JsonElement result = WriteToolHarness.Structured(await client.CallToolAsync("eplan_placement_delete", args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            Assert.Single(channel.Questions);
            Assert.Single(harness.Bridge.Writes);
            var call = Assert.Single(harness.Bridge.Calls, c => c.Operation == OperationNames.UnplacedDeleteExecute);
            JsonElement sent = JsonSerializer.SerializeToElement(call.Args);
            Assert.Equal(harness.ProjectPath, sent.GetProperty("projectPath").GetString());
            Assert.Equal("f1", sent.GetProperty("fingerprint").GetString());
            Assert.Equal("Line1", sent.GetProperty("request").GetProperty("project").GetString());
            Assert.Equal(JsonValueKind.Object, sent.GetProperty("backup").ValueKind);
        });
    }

    [Fact]
    public async Task MixedRoutesAndMissingPageLocatorsAreRefusedBeforeBridge()
    {
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny);
        await PreviewProtocolHarness.RunAsync(harness, async client =>
        {
            var args = new Dictionary<string, object?> { ["project"] = "Line1" };
            async Task<JsonElement> Error() => WriteToolHarness.Error(await client.CallToolAsync("eplan_placement_delete", args, cancellationToken: TestContext.Current.CancellationToken));
            JsonElement missing = await Error();
            Assert.Equal(ErrorReason.InvalidArgument, missing.GetProperty("reason").GetString());
            Assert.Contains("/page", missing.ToString(), StringComparison.Ordinal);
            Assert.Contains("/items", missing.ToString(), StringComparison.Ordinal);
            args["unplaced"] = _overlappingTags;
            args["page"] = null;
            args["items"] = Array.Empty<object>();
            JsonElement mixed = await Error();
            Assert.Equal(ErrorReason.InvalidArgument, mixed.GetProperty("reason").GetString());
            foreach (string path in _conflictingPaths)
            {
                Assert.Contains(path, mixed.ToString(), StringComparison.Ordinal);
            }
            Assert.Empty(harness.Bridge.Calls);
        });
    }

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
