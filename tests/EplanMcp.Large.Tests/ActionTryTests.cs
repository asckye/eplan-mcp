using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Large.Tests;

/// <summary>Attended only: open just the LargeWrite scratch copy, focus a page, and approve this trial in EPLAN.</summary>
[Trait("Size", "Large")]
public sealed class ActionTryTests
{
    [Fact]
    public async Task ScratchCopy_ZoomAction_LeavesDesignFingerprintUnchanged()
    {
        string home = Directory.CreateTempSubdirectory("eplanmcp-large-action-try-").FullName;
        try
        {
            var services = new ServiceCollection();
            services.AddLogging(l => l.ClearProviders());
            services.AddSingleton(new HostDataLocations(home));
            services.AddSingleton(new HostConfig([LiveEplan.ScratchRoot()], OutsideScratchWrites.Deny) { ActionTry = ActionRunSwitch.On });
            services.AddEplanMcp();
            await using var provider = services.BuildServiceProvider();
            var bridge = provider.GetRequiredService<IBridgeClient>();
            var projects = await bridge.InvokeAsync<object, ProjectListData>(OperationNames.ProjectList, new { }, TestContext.Current.CancellationToken);
            OpenProject copy = Assert.Single(projects.Projects);
            Assert.Equal(WriteHost.WritableName, copy.Name);
            Assert.True(copy.Current);
            Assert.True(ScratchPaths.IsInside(copy.Path, [WriteHost.ScratchFolder]));
            var reader = provider.GetRequiredService<ActionCatalogReader>();
            var catalog = await reader.ReadAsync(TestContext.Current.CancellationToken);
            var zoom = Assert.Single(catalog.Items.Where(i => i.Source == "ui" && i.Name == "XGedZoomAllAction" && i.Type is null).Take(1));
            Assert.Null(zoom.ExcludedReason);
            var tool = ActivatorUtilities.CreateInstance<ActionTryTools>(provider);
            var result = await tool.TryAsync(copy.Path, zoom.Name, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotEqual(true, result.IsError);
            JsonElement data = result.StructuredContent!.Value;
            Assert.Equal("committed", data.GetProperty("execution").GetString());
            Assert.True(data.GetProperty("unverified").GetBoolean());
            Assert.False(data.GetProperty("changed").GetBoolean());
            Assert.Equal(data.GetProperty("before").GetRawText(), data.GetProperty("after").GetRawText());
            var audit = Directory.GetFiles(provider.GetRequiredService<HostDataLocations>().Audit, "*.jsonl").SelectMany(File.ReadAllLines)
                .Select(line => JsonDocument.Parse(line).RootElement);
            Assert.Contains(audit, record => record.GetProperty("tool").GetString() == ActionTryTools.Tool && record.GetProperty("unverified").GetBoolean());
            // The tool's changed field compares the complete fingerprints, including files omitted from display.
        }
        finally { Directory.Delete(home, recursive: true); }
    }
}
