using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlanTaskAcceptanceScenariosTests : PlanProtocolFixture
{
    [Fact]
    public async Task Scenario3_ReviewedPlanSources_IdentifyTheCheckedObjectAndItsContentHash()
    {
        // Resource provenance is covered by WorkflowProtocolTests.Compile_ThroughMcp_PublishesHashEvidenceAuditAndTrustedOwner.
        await WithClient(async (client, _) =>
        {
            JsonElement compiled = ContextToolTests.Data(await Call(client, WorkflowTools.PlanTool, UpdateRequest()));
            JsonElement reviewed = ContextToolTests.Data(await Call(client, PlanTools.GetTool, new()
            {
                ["plan_id"] = compiled.GetProperty("planId").GetString(),
                ["response_format"] = "detailed",
                ["limit"] = 200
            }));
            JsonElement source = Assert.Single(reviewed.GetProperty("plan").GetProperty("sources").EnumerateArray());
            SnapshotObject original = _current.Pages[0].Objects[0];
            Assert.Equal(_current.Project.Path, source.GetProperty("project").GetString());
            Assert.Equal(original.Kind, source.GetProperty("kind").GetString());
            Assert.Equal(original.Type, source.GetProperty("type").GetInt32());
            Assert.Equal(original.Oid, source.GetProperty("oid").GetInt64());
            Assert.Equal(original.Hash, source.GetProperty("hash").GetString());
            Assert.Contains(reviewed.GetProperty("entries").EnumerateArray(), e => e.GetProperty("key").GetString() == "preconditions"
                && e.GetProperty("value").GetRawText().Contains(original.Hash, StringComparison.Ordinal));
            Assert.Empty(_fake.Writes);
        });
    }

    [Fact]
    public async Task Scenario4_ManualChangeAfterCompile_ApplyListsChangedPageAndWritesNothing()
    {
        await WithClient(async (client, _) =>
        {
            string id = ContextToolTests.Data(await Call(client, WorkflowTools.PlanTool, UpdateRequest())).GetProperty("planId").GetString()!;
            string before = _current.Pages[0].Hash;
            _current = PlanVerificationTestData.Copy(_current, pages => PlanVerificationTestData.Description(pages[0].Objects[0], "Manual change"));
            JsonElement error = ContextToolTests.Error(await Call(client, PlanApplyTools.ApplyTool, new()
            {
                ["plan_id"] = id
            }));
            Assert.Equal(ErrorReason.PlanStale, error.GetProperty("reason").GetString());
            JsonElement conflict = Assert.Single(error.GetProperty("metadata").GetProperty("conflicts").EnumerateArray());
            Assert.Equal("page:+A1/1", conflict.GetProperty("scope").GetString());
            Assert.Equal("changed", conflict.GetProperty("reason").GetString());
            Assert.Equal(before, conflict.GetProperty("expected").GetString());
            Assert.Equal(_current.Pages[0].Hash, conflict.GetProperty("actual").GetString());
            Assert.Empty(_fake.Writes);
            Assert.DoesNotContain(_fake.Calls, c => c.Operation == OperationNames.ProjectBackup);
        });
    }

    [Fact]
    public async Task Scenario5_SelectionChangesAfterCompile_ApplyUsesTheApprovedProjectAndPage()
    {
        string selectedPage = "+A1/1";
        _fake.Projects[0].Current = true;
        _fake.Handlers[OperationNames.PageUpdatePlan] = a =>
        {
            PageUpdateArgs args = Assert.IsType<PageUpdateArgs>(a);
            return Fresh("page.update", args.Pages.Count == 0 ? selectedPage : args.Pages[0].Name, args.Pages[0].Description);
        };
        await WithClient(async (client, _) =>
        {
            string id = ContextToolTests.Data(await Call(client, WorkflowTools.PlanTool, UpdateRequest())).GetProperty("planId").GetString()!;
            selectedPage = "+OTHER/99";
            _fake.Projects[0].Current = false;
            _fake.Projects.Add(new() { Name = "Other", Path = Path.Combine(_locations.DefaultScratch, "Other"), Current = true });
            JsonElement applied = ContextToolTests.Data(await Call(client, PlanApplyTools.ApplyTool, new()
            {
                ["plan_id"] = id
            }));
            Assert.Equal("committed", applied.GetProperty("execution").GetString());
            Assert.Equal("passed", applied.GetProperty("verification").GetString());
            PlanStepExecuteArgs sent = Assert.IsType<PlanStepExecuteArgs>(Assert.Single(_fake.Calls, c => c.Operation == OperationNames.PlanStepExecute).Args);
            Assert.Equal(_current.Project.Path, sent.PageUpdate!.Project);
            Assert.Equal("+A1/1", Assert.Single(sent.PageUpdate.Pages).Name);
            Assert.DoesNotContain(selectedPage, sent.Guard.ReadScope!.Pages);
            Assert.Single(_fake.Writes);
        });
    }

    [Theory]
    [InlineData("recovered", "partially_committed")]
    [InlineData("unknown", "unknown")]
    [InlineData("disconnected", "partially_committed")]
    public async Task Scenario7_DelayedCommitOrDisconnect_TaskSettlesWithoutResendingOrSendingNextStep(string interruption, string execution)
    {
        // Outcome folding is covered by Plans.PlanSettlementTests.StartedAfterDanglingPass_RemainsPending_UntilNextPassSettlesDelayedCommit;
        // MCP client disconnect is covered by PlanApplyProtocolTests.ClientDisconnect_StopsWaiting_AndWorkerCompletesWithOriginalTask.
        bool unknown = interruption == "unknown";
        _bridge.StepGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _fake.DuringWrite = _ =>
        {
            _current = PlanVerificationTestData.After("page.create", _current);
            _fake.WriteRecovered = interruption == "recovered";
            if (unknown)
            {
                _fake.WriteFailure = new()
                {
                    Reason = ErrorReason.OutcomeUnknown,
                    Message = "The commit response was lost; inspect the original plan.",
                    Execution = ExecutionState.Unknown
                };
            }
        };
        if (interruption == "disconnected")
        {
            _fake.Handlers[OperationNames.PlanStepExecute] = _ =>
            {
                _fake.Failure = new() { Reason = ErrorReason.EplanNotConnected, Message = "Disconnected between steps." };
                return JsonSerializer.SerializeToElement(new { });
            };
        }
        await WithClient(async (client, _) =>
        {
            string id = await Compile(client);
            JsonElement receipt = ContextToolTests.Data(await Call(client, PlanApplyTools.ApplyTool, new()
            {
                ["plan_id"] = id,
                ["wait_seconds"] = 0
            }));
            await _bridge.StepEntered.Task.WaitAsync(Token);
            try
            {
                JsonElement running = ContextToolTests.Data(await Call(client, PlanApplyTools.TaskTool, new()
                {
                    ["task_id"] = receipt.GetProperty("taskId").GetString(),
                    ["wait_seconds"] = 0
                }));
                Assert.Equal("running", running.GetProperty("status").GetString());
                Assert.Empty(_fake.Writes);
            }
            finally
            {
                _bridge.StepGate.TrySetResult();
            }
            JsonElement task = ContextToolTests.Data(await Call(client, PlanApplyTools.TaskTool, new()
            {
                ["task_id"] = receipt.GetProperty("taskId").GetString()
            }));
            Assert.Equal(unknown ? "unknown" : "failed", task.GetProperty("status").GetString());
            JsonElement saved = ContextToolTests.Data(await Call(client, PlanTools.GetTool, new()
            {
                ["plan_id"] = id
            }));
            Assert.Equal(execution, saved.GetProperty("result").GetProperty("execution").GetString());
            Assert.Equal(unknown ? 0 : 1, saved.GetProperty("summary").GetProperty("completedSteps").GetInt32());
            Assert.Single(_fake.Writes);
            Assert.Single(_fake.Calls, c => c.Operation == OperationNames.PlanStepExecute);
            Assert.Contains(_current.Pages, p => p.Name == "+A1/3");
            Assert.Equal("Power", _current.Pages.Single(p => p.Name == "+A1/3").Objects[0].Values.Single(v => v.Id == 11011).Texts![0].Text);
        });
    }

    [Fact]
    public async Task Scenario8_BadStepArguments_ReturnInvalidArgumentWithJsonPointersBeforeReading()
    {
        await WithClient(async (client, _) =>
        {
            JsonElement error = ContextToolTests.Error(await Call(client, WorkflowTools.PlanTool, new()
            {
                ["project"] = "Line1",
                ["steps"] = new object[] { new { op = "page.update", args = new { pages = Array.Empty<object>(), unexpected = true } } }
            }));
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
            JsonElement[] violations = [.. error.GetProperty("metadata").GetProperty("violations").EnumerateArray()];
            Assert.Contains(violations, v => v.GetProperty("pointer").GetString() == "/steps/0/args/pages");
            Assert.Contains(violations, v => v.GetProperty("pointer").GetString() == "/steps/0/args/unexpected");
            Assert.Empty(_fake.Calls);
        });
    }

    [Theory]
    [InlineData(false, ErrorReason.ResourceNotFound)]
    [InlineData(true, ErrorReason.CapabilityMissing)]
    public async Task Scenario8_MissingMacroOrBridgeOperation_ReturnsActionableErrorAndPublishesNothing(bool missingOperation, string reason)
    {
        _fake.Status.Handshake!.Operations = [.. _fake.Status.Handshake.Operations!, OperationNames.PlanMacroInspect, OperationNames.MacroInsertPlan, OperationNames.MacroInsertExecute];
        if (missingOperation)
        {
            _fake.Status.Handshake.Operations = [.. _fake.Status.Handshake.Operations.Where(op => op != OperationNames.PageUpdateExecute)];
        }
        _fake.Handlers[OperationNames.PlanMacroInspect] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.MacroNotFound,
            Message = "The requested macro is missing; choose an available resource and compile again."
        });
        await WithClient(async (client, _) =>
        {
            Dictionary<string, object?> request = missingOperation ? UpdateRequest() : WorkflowProtocolTests.Request("macro.insert");
            JsonElement error = ContextToolTests.Error(await Call(client, WorkflowTools.PlanTool, request));
            Assert.Equal(reason, error.GetProperty("reason").GetString());
            if (missingOperation)
            {
                Assert.Contains(error.GetProperty("metadata").GetProperty("missing").EnumerateArray(), m => m.GetString() == "op:page.update.execute");
            }
            Assert.Empty(_fake.Writes);
            string folder = Path.Combine(_locations.State, "plans");
            Assert.Empty(Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json") : []);
        });
    }

    private static Dictionary<string, object?> UpdateRequest() => new()
    {
        ["project"] = "Line1",
        ["steps"] = new object[]
        {
            new { op = "page.update", args = new { pages = new[] { new { name = "+A1/1", description = "New" } }, language = "en_US" } }
        }
    };
}
