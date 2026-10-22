using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Bridge;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>Real BridgeClient recovery over scripted channels: a broken execute is queried by ID, never sent twice.</summary>
[Trait("Size", "Medium")]
public sealed class ProjectCreateRecoveryTests
{
    [Theory]
    [InlineData("basic_project", OutcomeStatus.Committed)]
    [InlineData("backup", OutcomeStatus.Committed)]
    [InlineData("basic_project", OutcomeStatus.Unknown)]
    [InlineData("backup", OutcomeStatus.Unknown)]
    public async Task DisconnectAfterDelivery_QueriesTheOriginalRequestId_WithoutRepeatingExecutionOrCompatibility(string mode, string status)
    {
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny);
        string extension = mode == "backup" ? ".zw1" : ".zw9";
        Directory.CreateDirectory(harness.Locations.Imports);
        File.WriteAllBytes(Path.Combine(harness.Locations.Imports, "material" + extension), [1, 2, 3]);
        string execute = mode == "backup" ? OperationNames.ProjectRestoreExecute : OperationNames.ProjectCreateExecute;
        string plan = mode == "backup" ? OperationNames.ProjectRestorePlan : OperationNames.ProjectCreatePlan;
        string target = Path.Combine(harness.Locations.DefaultScratch, "projects", "Line1");
        string? requestId = null;
        int executes = 0, questions = 0;
        var adapter = new ProjectCreateBridge();
        var discovery = new FakeDiscovery();
        var endpoint = new BridgeEndpoint("EplanMcp.Recovery.fake", Environment.ProcessId);
        discovery.Endpoints.Add(endpoint);
        var connector = new FakeConnector(e => new FakeChannel(e, (_, parameters, _) =>
        {
            string operation = parameters!["operation"]!.GetValue<string>();
            object answer;
            if (operation == plan)
            {
                var args = parameters["args"]!.Deserialize<NewProjectArgs>(HostJson.Options)!;
                answer = mode == "backup" ? new ProjectRestorePlanOperation().Execute((object)args)! : new ProjectCreatePlanOperation().Execute((object)args)!;
            }
            else if (operation == execute)
            {
                executes++;
                requestId = parameters["correlationId"]!.GetValue<string>();
                adapter.Execute(parameters["args"]!.Deserialize<NewProjectArgs>(HostJson.Options)!, mode);
                throw new IOException("connection lost after delivery");
            }
            else
            {
                Assert.Equal(OperationNames.OutcomeGet, operation);
                questions++;
                Assert.Equal(requestId, parameters["args"]!["requestId"]!.GetValue<string>());
                answer = new OutcomeGetData { Status = status, Operation = execute };
            }
            return Task.FromResult<JsonNode?>(new JsonObject { ["data"] = JsonSerializer.SerializeToNode(answer, HostJson.Options) });
        })
        { Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(e.ProcessId, operations: [plan, execute, OperationNames.OutcomeGet])) });
        await using var bridge = new BridgeClient(discovery, connector, NullLogger<BridgeClient>.Instance, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
        var config = new HostConfig([harness.Locations.DefaultScratch], OutsideScratchWrites.Deny);
        var audit = new AuditLog(harness.Locations.Audit, TimeProvider.System);
        var suspensions = SuspensionStore.For(harness.Locations);
        var pipeline = new WritePipeline(bridge, config, suspensions, audit,
            new DanglingWrites(bridge, suspensions, audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new ApprovalService([], config, TimeProvider.System, NullLogger<ApprovalService>.Instance),
            new BackupFolders(harness.Locations, TimeProvider.System), TimeProvider.System, NullLogger<WritePipeline>.Instance);

        var result = await ProjectCreateTools.CreateAsync(pipeline, new(harness.Locations), config, mode, "material" + extension, "Line1", cancellationToken: TestContext.Current.CancellationToken);
        if (status == OutcomeStatus.Committed)
        {
            JsonElement data = WriteToolHarness.Structured(result);
            Assert.Equal("committed", data.GetProperty("execution").GetString());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("databaseVersion").ValueKind);
            Assert.Contains("metadata is unavailable", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        }
        else
        {
            JsonElement error = WriteToolHarness.Error(result);
            Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        }

        Assert.Equal((1, 1), (executes, questions));
        string link = Path.Combine(target, "Line1.elk");
        Assert.Equal(["make:" + link, "compatibility:" + link], adapter.Events);
        Assert.True(File.Exists(Path.Combine(target, "Line1.elk"))); // Unknown may still be writing, so keep the files.
    }
}
