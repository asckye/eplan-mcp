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
public sealed class PageDestructivePreviewTests
{
    [Theory]
    [InlineData(PageDestructiveTools.DeleteTool)]
    [InlineData(PageDestructiveTools.RenumberTool)]
    public async Task DefaultAndTruePreview_FalseApprovesAndExecutes(string tool)
    {
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(channel);
        string planOperation = tool == PageDestructiveTools.DeleteTool ? OperationNames.PageDeletePlan : OperationNames.PageRenumberPlan;
        string executeOperation = tool == PageDestructiveTools.DeleteTool ? OperationNames.PageDeleteExecute : OperationNames.PageRenumberExecute;
        harness.Bridge.Handlers[planOperation] = _ =>
        {
            WritePlan plan = harness.Plan([new() { Action = tool == PageDestructiveTools.DeleteTool ? "delete" : "update", Kind = "page", Name = "P", Property = tool == PageDestructiveTools.RenumberTool ? "page number" : null }]);
            plan.Details = new() { ["pages"] = "P\tCircuit\t1", ["orphanedCount"] = "0" };
            return plan;
        };
        harness.Bridge.Handlers[executeOperation] = args =>
        {
            BackupTarget backup = args is PageDeleteArgs delete ? delete.Backup! : ((PageRenumberArgs)args!).Backup!;
            File.WriteAllText(backup.Path, "copy");
            return tool == PageDestructiveTools.DeleteTool
                ? (object)new PageDeleteData { Project = "Line1", Pages = [new() { Name = "P", Type = "Circuit", Functions = 1 }] }
                : new PageRenumberData { Project = "Line1", Changed = [new() { From = "P", To = "P2" }] };
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
                var args = new Dictionary<string, object?> { ["project"] = "Line1", ["pages"] = new[] { "P" } };
                if (tool == PageDestructiveTools.RenumberTool)
                { args["start"] = 2; }
                if (dryRun.HasValue)
                { args["dry_run"] = dryRun.Value; }
                JsonElement data = WriteToolHarness.Structured(await client.CallToolAsync(tool, args, cancellationToken: Token));
                bool preview = dryRun != false;
                Assert.Equal(preview, data.GetProperty("dryRun").GetBoolean());
                Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, data.GetProperty("execution").GetString());
                Assert.Equal("f1", data.GetProperty("fingerprint").GetString());
                if (tool == PageDestructiveTools.RenumberTool)
                { Assert.Equal("renumber page P", Assert.Single(data.GetProperty("changes").EnumerateArray()).GetString()); }
                if (preview)
                {
                    Assert.Empty(channel.Questions);
                    Assert.Empty(harness.Bridge.Writes);
                    Assert.DoesNotContain(harness.Bridge.Calls, c => c.Operation == executeOperation);
                    Assert.False(Directory.Exists(harness.Locations.Backups));
                    Assert.False(Directory.Exists(harness.Locations.Audit));
                    Assert.Equal(JsonValueKind.Null, data.GetProperty("backup").ValueKind);
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
                    Assert.Contains(harness.Bridge.Calls, c => c.Operation == executeOperation);
                    Assert.True(File.Exists(data.GetProperty("backup").GetString()));
                    Assert.Equal("AI: " + tool, data.GetProperty("undo").GetString());
                    if (tool == PageDestructiveTools.DeleteTool)
                    { Assert.Equal("P", data.GetProperty("pages")[0].GetProperty("name").GetString()); }
                    else
                    { Assert.Equal("P2", data.GetProperty("changed")[0].GetProperty("to").GetString()); }
                }
            }
            Tool definition = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), t => t.Name == tool).ProtocolTool;
            Assert.True(definition.InputSchema.GetProperty("properties").GetProperty("dry_run").GetProperty("default").GetBoolean());
        }
        finally
        { await host.StopAsync(CancellationToken.None); }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
