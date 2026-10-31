using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlacementEditProtocolTests
{
    [Theory]
    [InlineData("dry")]
    [InlineData("success")]
    [InlineData("reject")]
    [InlineData("backup")]
    [InlineData("stale")]
    [InlineData("failure")]
    [InlineData("mismatch")]
    [InlineData("unknown")]
    [InlineData("policy")]
    [InlineData("invalid")]
    [InlineData("write")]
    [InlineData("recovered")]
    public async Task RegisteredTool_PreservesApprovalBackupAuditAndAtomicity(string scenario)
    {
        var channel = new FakeApprovalChannel("test", scenario == "reject" ? ApprovalDecision.Denied : ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny, channel);
        harness.Bridge.WriteRecovered = scenario == "recovered";
        var fake = new PlacementEditFixture(scenario == "policy" ? Path.Combine(harness.Locations.Root, "outside", "Line1") : harness.ProjectPath) { FailRollback = scenario == "unknown" };
        var planner = new PlacementEditOperation(fake, fake, false, fake.Backup);
        var execute = new PlacementEditExecuteOperation(fake, fake, (project, target) =>
        {
            if (scenario == "backup")
            { throw new BridgeErrorException(ErrorReason.BackupFailed, "Injected backup failure.") { Error = { Execution = ExecutionState.NotStarted } }; }
            fake.Backup(project, target);
        });
        object Invoke(IOperation operation, object? args)
        {
            try
            {
                var result = (JObject)operation.Execute(JObject.Parse(JsonSerializer.Serialize(args, HostJson.Options)))!;
                return operation == planner ? result.ToObject<WritePlan>()! : JsonNode.Parse(result.ToString())!.AsObject();
            }
            catch (BridgeErrorException error) { throw new BridgeCallException(error.Error); }
        }
        harness.Bridge.Handlers[OperationNames.PlacementEditPlan] = args => Invoke(planner, args);
        harness.Bridge.Handlers[OperationNames.PlacementEditExecute] = args => Invoke(execute, args);
        if (scenario == "stale")
        { harness.Bridge.DuringWrite = _ => fake.Data["objects"]![0]!["angle"] = 1d; }
        fake.Mutation = () =>
        {
            fake.Data["objects"]![0]!.Remove();
            if (scenario is "failure" or "unknown")
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Injected mid-batch failure."); }
            if (scenario == "mismatch")
            { fake.Data["objects"]![0]!["angle"] = 1d; }
        };
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(harness.Locations);
        builder.Services.AddSingleton(harness.Pipeline);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(harness.Bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            JObject input = fake.Input("delete");
            if (scenario == "write")
            { input = fake.Input("align", 0, 1); input["alignment"] = "left"; }
            input["dry_run"] = scenario == "dry";
            if (scenario == "invalid")
            { input["typo"] = 1; }
            var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(input.ToString())!;
            CallToolResult result = await client.CallToolAsync(PlacementEditTools.Tool, args, cancellationToken: Token);
            if (scenario is "dry" or "success" or "write" or "recovered")
            {
                JsonElement data = WriteToolHarness.Structured(result);
                Assert.Equal(scenario == "dry" ? ExecutionState.NotStarted : ExecutionState.Committed, data.GetProperty("execution").GetString());
                Assert.Equal(scenario == "success" ? 2 : 3, fake.Data["objects"]!.Count());
                if (scenario == "recovered")
                { Assert.Contains("without readback data", WriteToolHarness.Text(result, 0), StringComparison.Ordinal); }
            }
            else
            {
                JsonElement error = WriteToolHarness.Error(result);
                Assert.Equal(scenario switch { "reject" => ErrorReason.ApprovalDenied, "backup" => ErrorReason.BackupFailed, "stale" => ErrorReason.PlanStale, "unknown" => ErrorReason.OutcomeUnknown, "policy" => ErrorReason.PolicyDenied, "invalid" => ErrorReason.InvalidArgument, _ => ErrorReason.EplanOperationFailed }, error.GetProperty("reason").GetString());
                Assert.Equal(scenario == "unknown" ? ExecutionState.Unknown : scenario is "failure" or "mismatch" ? ExecutionState.RolledBack : ExecutionState.NotStarted, error.GetProperty("execution").GetString());
            }
            Assert.Equal(scenario is "dry" or "policy" or "invalid" or "write" ? 0 : 1, channel.Questions.Count);
            Assert.Equal(scenario is "success" or "write" or "failure" or "mismatch" or "unknown" ? 1 : 0, fake.Transactions);
            Assert.Equal(scenario is "success" or "write" ? 1 : 0, fake.Closed);
            string[] audits = Directory.Exists(harness.Locations.Audit) ? Directory.GetFiles(harness.Locations.Audit, "*.jsonl", SearchOption.AllDirectories) : [];
            string log = string.Join("\n", await Task.WhenAll(audits.Select(f => File.ReadAllTextAsync(f, Token))));
            if (scenario == "success")
            { Assert.Contains("execution_result", log, StringComparison.Ordinal); Assert.Contains("Destructive", log, StringComparison.Ordinal); Assert.Contains("backup", log, StringComparison.Ordinal); }
            if (scenario == "unknown")
            { Assert.Contains("writes_suspended", log, StringComparison.Ordinal); }
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }
    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
