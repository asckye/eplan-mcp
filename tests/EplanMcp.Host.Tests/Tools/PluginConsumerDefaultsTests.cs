using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class PluginConsumerDefaultsTests
{
    [Fact]
    public async Task ChangedBindingRevision_InvalidatesUsedPluginWithoutResolvingAgain()
    {
        using var harness = new WriteToolHarness();
        var packs = new PackLibrary(harness.Locations, HostConfig.Defaults(harness.Locations), new AuditLog(harness.Locations.Audit, TimeProvider.System), TimeProvider.System);
        ProjectBinding binding = new()
        {
            BindingVersion = 2,
            Project = harness.ProjectPath,
            Revision = 2,
            PackRoots = [],
            ProjectValues = new(),
            Cabinets = [new() { Id = "default", Layers = [], Values = new(), Structure = new() { ["all"] = true } }],
        };
        string folder = packs.Workspaces.FolderOf(binding.Project);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, ProjectBinding.FileName), PackJson.Serialize(binding));
        PluginResolution resolution = new(JsonSerializer.SerializeToElement(Array.Empty<object>()), [], [], JsonSerializer.SerializeToElement(Array.Empty<object>()))
        { BindingProject = harness.ProjectPath, BindingRevision = 1 };

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => PluginConsumerDefaults.BeforeExecute(packs, resolution, ["bound@1.0.0"])!(TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
        Assert.Null(PluginConsumerDefaults.BeforeExecute(packs, resolution, []));
    }
}
