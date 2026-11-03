using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Contracts;
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
public sealed class P02PagePreviewTests
{
    [Theory]
    [InlineData(PageWriteTools.CreateTool)]
    [InlineData(PageWriteTools.UpdateTool)]
    [InlineData(PageTextReplaceTools.Tool)]
    public async Task DefaultAndTruePreview_FalseApprovesAndReturnsReadback(string tool)
    {
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(channel);
        harness.ProjectPath = Path.Combine(harness.Locations.Root, "outside", "Line1");
        (string planOperation, string executeOperation) = tool switch
        {
            PageWriteTools.CreateTool => (OperationNames.PageCreatePlan, OperationNames.PageCreateExecute),
            PageWriteTools.UpdateTool => (OperationNames.PageUpdatePlan, OperationNames.PageUpdateExecute),
            _ => (OperationNames.PageTextReplacePlan, OperationNames.PageTextReplaceExecute),
        };
        var textChange = new PageTextChange { Page = "P", Oid = 1, Language = "en_US", Before = "Motor", After = "Drive", Occurrences = 1 };
        harness.Bridge.Handlers[planOperation] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = "update", Kind = "page", Name = "P", Property = "description (en_US)", OldValue = "Motor", NewValue = "Drive" }]);
            plan.PageTextChanges = [textChange];
            return plan;
        };
        harness.Bridge.Handlers[executeOperation] = args =>
        {
            Assert.Equal("f1", args switch
            {
                PageCreateArgs create => create.Fingerprint,
                PageUpdateArgs update => update.Fingerprint,
                PageTextReplaceArgs replace => replace.Fingerprint,
                _ => throw new InvalidOperationException("Unexpected execution arguments."),
            });
            return tool switch
            {
                PageWriteTools.CreateTool => (object)new PageCreateData { Project = "Line1", Pages = [new() { Name = "P", Type = "Circuit", Description = "Drive" }] },
                PageWriteTools.UpdateTool => new PageUpdateData { Project = "Line1", Language = "en_US", Pages = [new() { Name = "P", Previous = "Motor", Description = "Drive" }] },
                _ => new PageTextReplaceData { Project = "Line1", Changes = [textChange] },
            };
        };
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(harness.Locations);
        builder.Services.AddSingleton(harness.Pipeline);
        builder.Services.AddSingleton<IBridgeClient>(harness.Bridge);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            JsonElement? omitted = null;
            foreach (bool? dryRun in new bool?[] { null, true, false })
            {
                harness.Bridge.Calls.Clear();
                harness.Bridge.Writes.Clear();
                channel.Questions.Clear();
                var args = new Dictionary<string, object?> { ["project"] = "Line1", ["language"] = "en_US" };
                args["pages"] = tool == PageTextReplaceTools.Tool ? new[] { "P" } : (object)new[] { new { name = "P", description = "Drive" } };
                if (tool == PageTextReplaceTools.Tool)
                {
                    args["find"] = "Motor";
                    args["replace"] = "Drive";
                }
                if (dryRun.HasValue)
                { args["dry_run"] = dryRun.Value; }
                JsonElement data = WriteToolHarness.Structured(await client.CallToolAsync(tool, args, cancellationToken: Token));
                bool preview = dryRun != false;
                Assert.Equal(preview, data.GetProperty("dryRun").GetBoolean());
                Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, data.GetProperty("execution").GetString());
                Assert.Equal("f1", data.GetProperty("fingerprint").GetString());
                if (preview)
                {
                    Assert.Empty(channel.Questions);
                    Assert.Empty(harness.Bridge.Writes);
                    Assert.DoesNotContain(harness.Bridge.Calls, call => call.Operation == executeOperation);
                    Assert.False(Directory.Exists(harness.Locations.Audit));
                    Assert.False(Directory.Exists(harness.Locations.Backups));
                    Assert.Equal(JsonValueKind.Null, data.GetProperty("undo").ValueKind);
                    Assert.Contains("dry_run: false", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
                    if (omitted.HasValue)
                    { Assert.Equal(omitted.Value.GetRawText(), data.GetRawText()); }
                    omitted = data.Clone();
                }
                else
                {
                    Assert.Single(channel.Questions);
                    Assert.Single(harness.Bridge.Writes);
                    Assert.Contains(harness.Bridge.Calls, call => call.Operation == executeOperation);
                    Assert.Equal("AI: " + tool, data.GetProperty("undo").GetString());
                    if (tool == PageTextReplaceTools.Tool)
                    { Assert.Equal("Drive", data.GetProperty("changes")[0].GetProperty("after").GetString()); }
                    else
                    { Assert.Equal("Drive", data.GetProperty("pages")[0].GetProperty("description").GetString()); }
                }
            }
            Tool definition = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), item => item.Name == tool).ProtocolTool;
            Assert.True(definition.InputSchema.GetProperty("properties").GetProperty("dry_run").GetProperty("default").GetBoolean());
        }
        finally
        { await host.StopAsync(CancellationToken.None); }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
