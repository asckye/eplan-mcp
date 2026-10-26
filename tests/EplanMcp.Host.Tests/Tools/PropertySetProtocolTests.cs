using System.Globalization;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PropertySetProtocolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);
    private string _before = "old text";
    private PropertySetPlan? _planned;
    private bool _noChanges;
    private int _plans;

    [Fact]
    public async Task DryRun_ReturnsCompletePlanWithNoApprovalAuditOrExecute()
    {
        await WithClient(async client =>
        {
            CallToolResult call = await Call(client, dryRun: true);
            JsonElement data = Data(call);
            Assert.Equal(("not_started", 2, true), (data.GetProperty("execution").GetString(), data.GetProperty("changed_count").GetInt32(), data.GetProperty("dry_run").GetBoolean()));
            Assert.Equal(JsonValueKind.Null, data.GetProperty("undo").ValueKind);
            Assert.Equal("old text", data.GetProperty("changes")[0].GetProperty("before").GetString());
            Assert.Equal("new text", data.GetProperty("changes")[0].GetProperty("after").GetString());
            Assert.Equal("zh_CN", data.GetProperty("changes")[0].GetProperty("language").GetString());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("changes")[1].GetProperty("language").ValueKind);
            Assert.Equal(42, data.GetProperty("changes")[0].GetProperty("target").GetProperty("oid").GetInt64());
            Assert.Equal("Planned 2 property changes.", ((TextContentBlock)call.Content[0]).Text);
            Assert.Single(_harness.Bridge.Timeouts);
            Assert.Empty(_harness.Bridge.Writes);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
            Assert.Equal(1, _plans);
            Assert.True(PropertySetContractTests.AcceptsOutput(data));
        });
    }

    [Fact]
    public async Task NoChanges_SkipsApprovalAndExecute()
    {
        _noChanges = true;
        await WithClient(async client =>
        {
            JsonElement data = Data(await Call(client));
            Assert.Equal("not_started", data.GetProperty("execution").GetString());
            Assert.Equal(0, data.GetProperty("changed_count").GetInt32());
            Assert.Empty(_harness.Bridge.Writes);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
        }, outside: true);
    }

    [Fact]
    public async Task ScratchCommit_ExecutesOnceWithRawEvidenceAndAuditsKeysWithoutValues()
    {
        _before = "old\u202E" + new string('b', 550);
        await WithClient(async client =>
        {
            CallToolResult call = await Call(client);
            JsonElement data = Data(call);
            Assert.Equal(("committed", "AI: eplan_property_set"), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString()));
            Assert.Single(_harness.Bridge.Writes);
            PropertySetArgs execute = Assert.IsType<PropertySetArgs>(_harness.Bridge.Calls.Last().Args);
            Assert.Same(_planned, execute.Approved);
            Assert.Equal(_before, execute.Approved.Guards[0].Before[0].Text);
            Assert.Equal("multi_language", execute.Changes[0].Value.ValueType);
            Assert.Equal("text", execute.Changes[1].Value.ValueType);
            Assert.DoesNotContain('\u202E', data.GetRawText());
            Assert.Contains("553 characters in total", data.GetProperty("changes")[0].GetProperty("before").GetString(), StringComparison.Ordinal);
            List<JsonObject> records = Records(_harness);
            JsonObject started = records.Single(r => r["kind"]!.GetValue<string>() == "execution_started");
            Assert.Equal(64, started["fingerprint"]!.GetValue<string>().Length);
            Assert.Equal(2, started["changedCount"]!.GetValue<int>());
            Assert.Equal(20011, started["changes"]![0]!["property_id"]!.GetValue<int>());
            Assert.Equal("zh_CN", started["changes"]![0]!["language"]!.GetValue<string>());
            Assert.Equal(64, started["arguments"]!["argumentsHash"]!.GetValue<string>().Length);
            Assert.DoesNotContain("old", started.ToJsonString(), StringComparison.Ordinal);
            Assert.DoesNotContain("new text", started.ToJsonString(), StringComparison.Ordinal);
            JsonObject outcome = records.Single(r => r["kind"]!.GetValue<string>() == "execution_result");
            Assert.Equal("committed", outcome["execution"]!.GetValue<string>());
            Assert.Equal(2, outcome["changedCount"]!.GetValue<int>());
            Assert.Equal("Changed 2 properties; Ctrl+Z in EPLAN undoes this step.", ((TextContentBlock)call.Content[0]).Text);
        });
    }

    [Theory]
    [InlineData(false, ErrorReason.ApprovalDenied)]
    [InlineData(true, ErrorReason.ApprovalExpired)]
    public async Task OutsideScratch_RefusedApproval_ListsEveryChangeAndNeverExecutes(bool expired, string reason)
    {
        var channel = new FakeApprovalChannel("engineer", expired ? ApprovalDecision.Expired : ApprovalDecision.Denied);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Approve, channel);
        _before = "old\u202E\n" + new string('x', 200);
        await WithClient(async client =>
        {
            JsonElement error = Error(await Call(client));
            Assert.Equal((reason, "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            ApprovalQuestion question = Assert.Single(channel.Questions);
            Assert.Equal(ToolTier.Write, question.Tier);
            ApprovalAskArgs approval = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US"));
            Assert.Equal(2, approval.Changes.Count);
            Assert.Contains("K1", approval.Changes[0], StringComparison.Ordinal);
            Assert.Contains("20011 (zh_CN)", approval.Changes[0], StringComparison.Ordinal);
            Assert.Contains("old", approval.Changes[0], StringComparison.Ordinal);
            Assert.Contains("new text", approval.Changes[0], StringComparison.Ordinal);
            Assert.Contains("characters", approval.Changes[0], StringComparison.Ordinal);
            Assert.DoesNotContain('\u202E', approval.Changes[0]);
            Assert.DoesNotContain('\n', approval.Changes[0]);
            Assert.Empty(harness.Bridge.Writes);
            Assert.DoesNotContain("new text", Records(harness).Single().ToJsonString(), StringComparison.Ordinal);
        }, outside: true, harness);
    }

    [Fact]
    public async Task OutsideScratch_GrantedApproval_CommitsOnce()
    {
        var channel = new FakeApprovalChannel("engineer", ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Approve, channel);
        await WithClient(async client =>
        {
            Assert.Equal("committed", Data(await Call(client)).GetProperty("execution").GetString());
            Assert.Single(channel.Questions);
            Assert.Single(harness.Bridge.Writes);
            Assert.Equal(3, Records(harness).Count);
        }, outside: true, harness);
    }

    [Theory]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    public async Task ExecuteFailure_PreservesReasonAndExecution(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = reason,
            Execution = execution,
            Message = "Re-plan before retrying.",
        };
        await WithClient(async client =>
        {
            JsonElement error = Error(await Call(client));
            Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            Assert.Single(_harness.Bridge.Writes);
            Assert.Equal(execution, Records(_harness).Last()["execution"]!.GetValue<string>());
        });
    }

    [Fact]
    public async Task OutsideScratch_DenyPolicy_AllowsPreviewButRefusesExecute()
    {
        await WithClient(async client =>
        {
            Assert.Equal(2, Data(await Call(client, dryRun: true)).GetProperty("changed_count").GetInt32());
            Assert.Equal(ErrorReason.PolicyDenied, Error(await Call(client)).GetProperty("reason").GetString());
            Assert.Empty(_harness.Bridge.Writes);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
        }, outside: true);
    }

    [Fact]
    public async Task NotWhitelisted_RefusesBeforeBridgePlanningOrExecute()
    {
        await WithClient(async client =>
        {
            Dictionary<string, object?> args = Args();
            args["changes"] = JsonSerializer.Deserialize<JsonElement>("""
                [{"target":{"device_tag":"K1"},"property":{"property_id":20024},"value":"new text"}]
                """);
            JsonElement error = Error(await client.CallToolAsync(PropertySetTools.Tool, args, cancellationToken: Token));
            Assert.Equal((ErrorReason.PolicyDenied, "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            Assert.Equal(0, _plans);
            Assert.Empty(_harness.Bridge.Calls);
            Assert.Empty(_harness.Bridge.Writes);
        });
    }

    [Fact]
    public async Task RecoveredCommit_ReturnsOriginalPlanWithRecoveryWarning()
    {
        _harness.Bridge.WriteRecovered = true;
        await WithClient(async client =>
        {
            JsonElement result = Data(await Call(client));
            Assert.Equal("committed", result.GetProperty("execution").GetString());
            Assert.True(result.GetProperty("recovered").GetBoolean());
            Assert.Contains("original plan", result.GetProperty("warnings")[0].GetString(), StringComparison.Ordinal);
            Assert.Equal(2, result.GetProperty("changed_count").GetInt32());
            Assert.Single(_harness.Bridge.Writes);
        });
    }

    [Fact]
    public async Task BudgetOverflow_RefusesBeforeApprovalAuditOrExecute()
    {
        await WithClient(async client =>
        {
            Dictionary<string, object?> args = Args();
            args["changes"] = Enumerable.Range(0, 40).Select(i => new
            {
                target = new { device_tag = "K" + i.ToString(CultureInfo.InvariantCulture) },
                property = new { property_id = 20027 },
                value = new string('界', 500),
            }).ToArray();
            Assert.Equal(ErrorReason.InvalidArgument, Error(await client.CallToolAsync(PropertySetTools.Tool, args, cancellationToken: Token)).GetProperty("reason").GetString());
            Assert.Empty(_harness.Bridge.Writes);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
        });
    }

    private async Task WithClient(Func<McpClient, Task> check, bool outside = false, WriteToolHarness? harness = null)
    {
        WriteToolHarness environment = harness ?? _harness;
        environment.ProjectPath = outside ? Path.Combine(environment.Locations.Root, "outside", "Line1") : environment.ProjectPath;
        environment.Bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new OpenProject { Name = "Line1", Path = environment.ProjectPath } };
        environment.Bridge.Handlers[OperationNames.PropertySetPlan] = raw => Plan(environment, Assert.IsType<PropertySetArgs>(raw));
        environment.Bridge.Handlers[OperationNames.PropertySetExecute] = raw =>
        {
            PropertySetArgs args = Assert.IsType<PropertySetArgs>(raw);
            return new PropertySetData { Plan = args.Approved.Plan, Execution = ExecutionState.Committed, Undo = "AI: eplan_property_set" };
        };
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(environment.Locations);
        builder.Services.AddSingleton(environment.Pipeline);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(environment.Bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            await check(client);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private PropertySetPlan Plan(WriteToolHarness harness, PropertySetArgs args)
    {
        _plans++;
        var result = new PropertySetPlan { Plan = harness.Plan([]) };
        result.Plan.Fingerprint = new string('a', 64);
        foreach (PropertySetChange change in args.Changes)
        {
            int id = change.Property.PropertyId ?? 20027;
            string language = change.Language ?? TextLanguages.Independent;
            var guard = new PropertySetGuard
            {
                Target = new ObjectLocator { Project = harness.ProjectPath, Kind = "function", Name = change.Target.DeviceTag, Type = 17, Oid = 42, Category = "general", Page = "P1" },
                PropertyId = id,
                Main = true,
                Before = [new SnapshotText { Language = language, Text = _before }],
                After = [new SnapshotText { Language = language, Text = _noChanges ? _before : change.Value.Text! }],
            };
            result.Guards.Add(guard);
            if (!_noChanges)
            {
                result.Plan.Changes.Add(new PlannedChange
                {
                    Action = "update",
                    Kind = "function",
                    Name = guard.Target.Name!,
                    Property = id.ToString(CultureInfo.InvariantCulture) + " (" + language + ")",
                    OldValue = _before,
                    NewValue = change.Value.Text,
                });
            }
        }
        _planned = result;
        return result;
    }

    private static Dictionary<string, object?> Args(bool dryRun = false) => new()
    {
        ["project"] = "Line1",
        ["changes"] = JsonSerializer.Deserialize<JsonElement>("""
            [{"target":{"device_tag":"K1"},"property":{"property_id":20011},"language":"zh_CN","value":"new text"},
             {"target":{"device_tag":"K1"},"property":{"property_name":"FUNC_TECHNICAL_CHARACTERISTIC"},"value":"DC 24V"}]
            """),
        ["dry_run"] = dryRun,
    };

    private static Task<CallToolResult> Call(McpClient client, bool dryRun = false) => client.CallToolAsync(PropertySetTools.Tool, Args(dryRun), cancellationToken: Token).AsTask();
    private static JsonElement Error(CallToolResult call) => WriteToolHarness.Error(call);
    private static JsonElement Data(CallToolResult call)
    {
        JsonElement data = WriteToolHarness.Structured(call);
        Assert.True(JsonElement.DeepEquals(data, JsonSerializer.Deserialize<JsonElement>(((TextContentBlock)call.Content[1]).Text)));
        return data;
    }
    private static List<JsonObject> Records(WriteToolHarness harness) => [.. Directory.EnumerateFiles(harness.Locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public void Dispose() => _harness.Dispose();
}
