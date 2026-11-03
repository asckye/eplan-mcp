using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

public sealed partial class PluginResolverTests
{
    [Fact]
    public async Task ProjectBinding_UsesExactCabinetLayerAndSuppressesGlobalVersion()
    {
        WritePack("plugin.first", "1.0.0", 7);
        WritePack("plugin.first", "2.0.0", 8);
        await EnableAsync(new PluginIdentity("plugin.first", "1.0.0"));
        string project = PlanSnapshotHashes.ProjectKey(@"D:\Fixture\Project");
        var binding = Binding() with
        {
            Project = project,
            Locks = [new() { Id = "plugin.first", Version = "2.0.0", Digest = new PackStore(_locations.Packs).InstalledDigest("plugin.first", "2.0.0")! }],
            Cabinets = [new() { Id = "default", Layers = ["plugin.first@2.0.0:config_layer/defaults"], Values = new(), Structure = new() { ["all"] = true } }]
        };
        string folder = new WorkspaceStore(_locations.Workspaces, new(_locations.Packs)).FolderOf(project);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, ProjectBinding.FileName), PackJson.Serialize(binding), Cancel);

        PluginResolution result = await Resolver.ResolveAsync(project, Cancel, page: "+A/1");

        Assert.Equal(8, Value(result));
        Assert.Contains(result.Enabled, p => p.Version == "2.0.0" && p.Scope == "cabinet");
        Assert.Contains(result.Warnings, warning => warning.Contains("skipped", StringComparison.Ordinal));
        Assert.Equal("plugin.first@2.0.0:config_layer/defaults", result.Values[0].GetProperty("winner").GetProperty("source").GetProperty("resource").GetString());
    }

    [Fact]
    public async Task UnreadableProjectBinding_KeepsGlobalValuesAndWarns()
    {
        WritePack("plugin.first", "1.0.0", 7);
        await EnableAsync(new PluginIdentity("plugin.first", "1.0.0"));
        const string project = @"D:\Fixture\Project";
        string folder = new WorkspaceStore(_locations.Workspaces, new(_locations.Packs)).FolderOf(PlanSnapshotHashes.ProjectKey(project));
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, ProjectBinding.FileName), "{bad", Cancel);
        PluginResolution result = await Resolver.ResolveAsync(project, Cancel);
        Assert.Equal(7, Value(result));
        Assert.Contains(result.Warnings, w => w.StartsWith("Project binding", StringComparison.Ordinal));
    }
}
