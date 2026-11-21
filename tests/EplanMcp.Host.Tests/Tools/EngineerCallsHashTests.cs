using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Project;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// ADR-0065 decision 3: the Workbench answers the Host's question for the engineer's click only when the arguments hash
/// in the question equals the one the Workbench computed. Each button call goes through the real MCP binder and the
/// real tool here, so a change in how a tool builds its arguments breaks this test rather than the button.
/// </summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class EngineerCallsHashTests
{
    [Fact]
    public Task ReportUpdate_AsksWithTheWorkbenchsHash() => AskedHashIsAsync(EngineerCalls.ReportUpdate("Line1"), OperationNames.ReportUpdatePlan);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CheckRun_AsksWithTheWorkbenchsHash(bool onePage) =>
        AskedHashIsAsync(EngineerCalls.CheckRun("Line1", onePage ? "=A1&EFS1/1" : null), OperationNames.CheckRunPlan);

    private static async Task AskedHashIsAsync(EngineerCall call, string plan)
    {
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Denied);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Approve, channel);
        harness.ProjectPath = Path.Combine(harness.Locations.Root, "outside", "Line1");
        harness.Bridge.Handlers[plan] = _ => harness.Plan([new PlannedChange { Action = "update", Kind = "project", Name = "Line1" }]);

        await PreviewProtocolHarness.RunAsync(harness, async client =>
        {
            Dictionary<string, object?> args = call.Arguments.ToDictionary(a => a.Key, a => (object?)JsonSerializer.SerializeToElement(a.Value));
            args["dry_run"] = false;
            await client.CallToolAsync(call.Tool, args, cancellationToken: TestContext.Current.CancellationToken);
        });

        ApprovalQuestion question = Assert.Single(channel.Questions);
        Assert.Equal((call.Tool, call.ArgumentsHash), (question.Tool, question.ArgumentsHash));
    }
}
