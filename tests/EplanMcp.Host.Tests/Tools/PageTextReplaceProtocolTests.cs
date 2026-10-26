using System.Globalization;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
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
public sealed class PageTextReplaceProtocolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);
    private readonly PageTextStore _store = new();
    private Action? _beforeWrite;

    [Fact]
    public async Task InvalidArguments_RealMcp_IncludeNotStartedAndNeverReachBridge()
    {
        await WithClient(async client =>
        {
            Dictionary<string, object?> invalid = Args();
            invalid["find"] = string.Empty;
            JsonElement valueError = Error(await client.CallToolAsync(PageTextReplaceTools.Tool, invalid, cancellationToken: Token));
            Assert.Equal("not_started", valueError.GetProperty("execution").GetString());
            invalid = Args();
            invalid.Remove("language");
            invalid["misspelled"] = true;
            JsonElement shapeError = Error(await client.CallToolAsync(PageTextReplaceTools.Tool, invalid, cancellationToken: Token));
            Assert.Equal("INVALID_ARGUMENT", shapeError.GetProperty("reason").GetString());
            Assert.Equal("not_started", shapeError.GetProperty("execution").GetString());
            Assert.Equal(0, _harness.Bridge.Invocations);
            Assert.Empty(_store.Writes);
        });
    }

    [Fact]
    public async Task DryRun_RealMcp_ReturnsEveryMatchWithoutApprovalAuditOrExecution()
    {
        await WithClient(async client =>
        {
            CallToolResult result = await Call(client, dryRun: true);
            JsonElement data = Data(result);
            Assert.Equal(("not_started", 3, 4), (data.GetProperty("execution").GetString(), data.GetProperty("totalChanges").GetInt32(), data.GetProperty("totalOccurrences").GetInt32()));
            Assert.Equal("Motor Motor", data.GetProperty("changes")[0].GetProperty("before").GetString());
            Assert.Equal("Drive Drive", data.GetProperty("changes")[0].GetProperty("after").GetString());
            Assert.Equal("Dry run: would change 3 text-language entries (4 occurrences). Nothing was changed.", ((TextContentBlock)result.Content[0]).Text);
            Assert.Empty(_store.Writes);
            Assert.Empty(_harness.Bridge.Writes);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
        });
    }

    [Fact]
    public async Task Commit_RealMcp_WritesOnceAndAuditsCompleteLocatorsCountsAndFingerprint()
    {
        await WithClient(async client =>
        {
            JsonElement result = Data(await Call(client));
            Assert.Equal(("committed", "AI: eplan_page_text_replace"), (result.GetProperty("execution").GetString(), result.GetProperty("undo").GetString()));
            Assert.Single(_harness.Bridge.Writes);
            JsonObject started = Records(_harness).Single(r => r["kind"]!.GetValue<string>() == "execution_started");
            Assert.Equal(64, started["fingerprint"]!.GetValue<string>().Length);
            Assert.Equal(3, started["totalChanges"]!.GetValue<int>());
            Assert.Equal(4, started["totalOccurrences"]!.GetValue<long>());
            Assert.Equal("en_US", started["textChanges"]![0]!["language"]!.GetValue<string>());
            Assert.Equal(1, started["textChanges"]![0]!["oid"]!.GetValue<long>());
        });
    }

    [Fact]
    public async Task Stale_RealMcp_ReturnsNotStartedAndWritesNothing()
    {
        _beforeWrite = () => _store.Pages.Remove(PageTextStore.OtherPage);
        await WithClient(async client =>
        {
            JsonElement error = Error(await Call(client));
            Assert.Equal(("PLAN_STALE", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            Assert.Empty(_store.Writes);
            Assert.Empty(_store.Calls);
        });
    }

    [Fact]
    public async Task Cap_RealMcp_RefusesCompleteBatchBeforeAuditOrExecution()
    {
        await WithClient(async client =>
        {
            Dictionary<string, object?> args = Args();
            args["max_changes"] = 1;
            JsonElement error = Error(await client.CallToolAsync(PageTextReplaceTools.Tool, args, cancellationToken: Token));
            Assert.Equal("INVALID_ARGUMENT", error.GetProperty("reason").GetString());
            Assert.Equal("1", error.GetProperty("metadata").GetProperty("limit").GetString());
            Assert.Empty(_store.Writes);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
        });
    }

    [Fact]
    public async Task OutsideScratch_PolicyDeniesWriteButAllowsDryRun()
    {
        await WithClient(async client =>
        {
            Assert.Equal("not_started", Data(await Call(client, dryRun: true)).GetProperty("execution").GetString());
            Assert.Equal("POLICY_DENIED", Error(await Call(client)).GetProperty("reason").GetString());
            Assert.Empty(_store.Writes);
            Assert.False(Directory.Exists(_harness.Locations.Audit));
        }, outside: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OutsideScratch_ApprovalPolicy_PresentsTruncatedPageOidLanguageAndBothValues(bool approve)
    {
        var channel = new FakeApprovalChannel("engineer", approve ? ApprovalDecision.Granted : ApprovalDecision.Denied);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Approve, channel);
        string longText = "Motor" + new string('x', 200) + "\u202E\n";
        _store.Pages[PageTextStore.OtherPage] = new(PageTextStore.OtherPage, "page:2", [PageTextStore.Text(3, new TextEntry("en_US", longText))]);
        await WithClient(async client =>
        {
            CallToolResult result = await Call(client);
            ApprovalQuestion question = Assert.Single(channel.Questions);
            ApprovalAskArgs form = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US"));
            Assert.Equal(ToolTier.Write, question.Tier);
            Assert.Contains(form.Changes, line => line.Contains(PageTextStore.OtherPage, StringComparison.Ordinal) && line.Contains("text 3 (en_US)", StringComparison.Ordinal) && line.Contains("Motor", StringComparison.Ordinal) && line.Contains("Drive", StringComparison.Ordinal));
            Assert.DoesNotContain(form.Changes, line => line.Contains(new string('x', 200), StringComparison.Ordinal) || line.Contains('\u202E', StringComparison.Ordinal));
            Assert.Equal(approve, result.IsError != true);
            Assert.Equal(approve ? 3 : 0, _store.Writes.Count);
        }, outside: true, harness);
    }

    [Fact]
    public async Task LongPreview_CleansFieldsAndTruncatesRowsWithinDoubleJsonBudgetWithFullCounts()
    {
        _store.Pages[PageTextStore.Page] = new(PageTextStore.Page, "page:1",
            [.. Enumerable.Range(1, 500).Select(n => PageTextStore.Text(n, new TextEntry("en_US", "Motor\u202E" + new string('界', 700))))]);
        await WithClient(async client =>
        {
            Dictionary<string, object?> args = Args(dryRun: true);
            args["pages"] = new[] { PageTextStore.Page };
            CallToolResult response = await client.CallToolAsync(PageTextReplaceTools.Tool, args, cancellationToken: Token);
            JsonElement data = Data(response);
            Assert.True(data.GetProperty("truncated").GetBoolean());
            Assert.Equal(500, data.GetProperty("totalChanges").GetInt32());
            Assert.Equal(500, data.GetProperty("totalOccurrences").GetInt32());
            Assert.Equal(500, data.GetProperty("omittedChanges").GetInt32() + data.GetProperty("changes").GetArrayLength());
            Assert.True(data.GetProperty("omittedChanges").GetInt32() > 0);
            Assert.All(data.GetProperty("changes").EnumerateArray(), c =>
            {
                Assert.DoesNotContain('\u202E', c.GetProperty("before").GetString()!);
                Assert.Contains("705 characters in total", c.GetProperty("before").GetString(), StringComparison.Ordinal);
            });
            Assert.True(ResultShaper.ResultTokens(data.GetRawText()) + ResultShaper.EstimateTokens(((TextContentBlock)response.Content[0]).Text) <= ResultShaper.DefaultTokenBudget);
            Assert.Empty(_store.Writes);
        });
    }

    [Fact]
    public async Task RecoveredCommit_ReturnsOriginalPlanAndRecoveryHint()
    {
        _harness.Bridge.WriteRecovered = true;
        await WithClient(async client =>
        {
            JsonElement data = Data(await Call(client));
            Assert.Equal("committed", data.GetProperty("execution").GetString());
            Assert.True(data.GetProperty("recovered").GetBoolean());
            Assert.Contains("original plan", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
            Assert.Equal(3, data.GetProperty("changes").GetArrayLength());
        });
    }

    private async Task WithClient(Func<McpClient, Task> check, bool outside = false, WriteToolHarness? harness = null)
    {
        WriteToolHarness environment = harness ?? _harness;
        _store.Project.Path = outside ? Path.Combine(environment.Locations.Root, "outside", "Line1") : environment.ProjectPath;
        var router = new OperationRouter(
            [new PageTextReplacePlanOperation(_store, _store), new PageTextReplaceExecuteOperation(_store, _store, _store)],
            new Dispatcher(), null, new Log(), TimeSpan.FromSeconds(1));
        environment.Bridge.Handlers[OperationNames.PageTextReplacePlan] = args => Invoke<WritePlan>(router, OperationNames.PageTextReplacePlan, args, "plan");
        environment.Bridge.Handlers[OperationNames.PageTextReplaceExecute] = args => Invoke<PageTextReplaceData>(router, OperationNames.PageTextReplaceExecute, args, environment.Bridge.Writes[^1].RequestId);
        environment.Bridge.DuringWrite = _ => _beforeWrite?.Invoke();
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

    private static T Invoke<T>(OperationRouter router, string operation, object? args, string requestId)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(router.Invoke(operation, JToken.Parse(JsonSerializer.Serialize(args, HostJson.Options)), requestId).ToString(), HostJson.Options)!;
        }
        catch (BridgeErrorException ex)
        {
            throw new BridgeCallException(ex.Error);
        }
    }

    private static Dictionary<string, object?> Args(bool dryRun = false) => new()
    {
        ["project"] = "Line1",
        ["pages"] = new[] { PageTextStore.Page, PageTextStore.OtherPage },
        ["find"] = "Motor",
        ["replace"] = "Drive",
        ["language"] = "en_US",
        ["dry_run"] = dryRun,
    };

    private static Task<CallToolResult> Call(McpClient client, bool dryRun = false) => client.CallToolAsync(PageTextReplaceTools.Tool, Args(dryRun), cancellationToken: Token).AsTask();
    private static JsonElement Data(CallToolResult result) => WriteToolHarness.Structured(result);
    private static JsonElement Error(CallToolResult result) => WriteToolHarness.Error(result);
    private static List<JsonObject> Records(WriteToolHarness harness) => [.. Directory.EnumerateFiles(harness.Locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public void Dispose() => _harness.Dispose();

    private sealed class Dispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            try
            {
                return DispatchResult.Completed(work());
            }
            catch (Exception ex)
            {
                return DispatchResult.Faulted(ex);
            }
        }
    }

    private sealed class Log : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}
