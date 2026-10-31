using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Large.Tests;

/// <summary>Prepared LargeWrite scratch copy only; the trial approval is answered by the script and must be asked.</summary>
[Trait("Size", "Large")]
public sealed class ActionTryTests(LiveEplan eplan)
{
    [Fact]
    public async Task ScratchCopy_ZoomAction_LeavesDesignFingerprintUnchanged()
    {
        string home = Directory.CreateTempSubdirectory("eplanmcp-large-action-try-").FullName;
        try
        {
            // UI actions act on the current project: the trial requires the scratch copy to be the only open project.
            await using PlanStepLiveSession alone = await PlanStepLiveSession.StartAsync(eplan);
            string project = alone.Project;
            Assert.True(ScratchPaths.IsInside(project, [LiveEplan.ScratchRoot()]));
            Dictionary<string, string> files = LiveSnapshots.DesignFiles(project);
            var approvals = new TrialApprovals(ActionTryTools.Tool);
            var services = new ServiceCollection();
            services.AddLogging(l => l.ClearProviders());
            services.AddSingleton(new HostDataLocations(home));
            services.AddSingleton(new HostConfig([LiveEplan.ScratchRoot()], OutsideScratchWrites.Deny) { ActionTry = ActionRunSwitch.On });
            services.AddEplanMcp();
            services.RemoveAll<IApprovalChannel>();
            services.AddSingleton<IApprovalChannel>(approvals);
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IBridgeClient>().SelectAsync(eplan.ProcessId, TestContext.Current.CancellationToken);
            var catalog = await provider.GetRequiredService<ActionCatalogReader>().ReadAsync(TestContext.Current.CancellationToken);
            var zoom = catalog.Items.First(i => i.Source == "ui" && i.Name == "XGedZoomAllAction" && i.Type is null);
            Assert.Null(zoom.ExcludedReason);
            // Zoom needs an open graphical page editor; without one EPLAN reports failure and the trial rolls back.
            string page = (await alone.SnapshotAsync()).Pages.Select(p => p.Name).Order(StringComparer.Ordinal).First();
            await provider.GetRequiredService<IBridgeClient>().InvokeAsync<PageOpenArgs, PageOpenData>(OperationNames.PageOpen, new() { Project = project, Page = page }, TestContext.Current.CancellationToken);
            var tool = ActivatorUtilities.CreateInstance<ActionTryTools>(provider);
            var result = await tool.TryAsync(project, zoom.Name, cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(result.IsError == true, string.Join("\n", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text)));
            JsonElement data = result.StructuredContent!.Value;
            Assert.Equal("committed", data.GetProperty("execution").GetString());
            Assert.True(data.GetProperty("unverified").GetBoolean());
            Assert.False(data.GetProperty("changed").GetBoolean());
            Assert.Equal(data.GetProperty("before").GetRawText(), data.GetProperty("after").GetRawText());
            Assert.Equal(ActionTryTools.Tool, Assert.Single(approvals.Questions).Tool);
            var audit = Directory.GetFiles(provider.GetRequiredService<HostDataLocations>().Audit, "*.jsonl").SelectMany(File.ReadAllLines)
                .Select(line => JsonDocument.Parse(line).RootElement);
            Assert.Contains(audit, record => record.GetProperty("tool").GetString() == ActionTryTools.Tool
                && record.TryGetProperty("unverified", out JsonElement unverified) && unverified.GetBoolean());
            Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
        }
        finally { Directory.Delete(home, recursive: true); }
    }
}
