using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Packs;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class BuiltinPluginLibraryTests
{
    [Fact]
    public async Task BuildOutput_InstallsOfflineAndResolvesTdefWithoutApproval()
    {
        string root = Directory.CreateTempSubdirectory("eplanmcp-builtin-").FullName;
        try
        {
            var locations = new HostDataLocations(root);
            HostConfig config = HostConfig.Defaults(locations);
            var state = new PluginStateStore(locations);
            var source = new PluginLibrarySource(locations, state);
            (IReadOnlyList<PluginLibraryReadResult> libraries, _) = await source.ReadAsync(TestContext.Current.CancellationToken);
            PluginLibraryReadResult builtin = Assert.Single(libraries, library => library.SourceType == "builtin");
            Assert.Empty(builtin.Warnings);
            PluginLibraryEntry entry = Assert.Single(builtin.Index!.Plugins, plugin => plugin.Id == "iec-base");
            var packs = new PackLibrary(locations, config, new AuditLog(locations.Audit, TimeProvider.System), TimeProvider.System);
            var bridge = new FakeBridgeClient { Status = new BridgeStatus([], null, null) };
            var denied = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Denied);
            var approvals = new ApprovalService([denied], config, TimeProvider.System, NullLogger<ApprovalService>.Instance);
            var result = await new PackInstaller(packs, approvals, bridge).InstallLibraryAsync(builtin, entry, null, TestContext.Current.CancellationToken);
            Assert.Null(result.Error);
            Assert.Equal(PackInstallOutcome.Installed, result.Installed?.Outcome);
            Assert.Equal(entry.Digest, result.Digest);
            Assert.Empty(denied.Questions);

            (PluginState current, string? revision) = await state.ReadAsync(TestContext.Current.CancellationToken);
            await state.WriteAsync(current with { Enabled = [new(entry.Id, entry.Version)] }, revision, TestContext.Current.CancellationToken);
            PluginResolution resolved = await new PluginResolver(packs).ResolveAsync(TestContext.Current.CancellationToken);
            Assert.Empty(resolved.Warnings);
            Assert.Equal(["iec-base@1.0.0"], resolved.Plugins);
            Assert.Equal("TDEF", resolved.Values.EnumerateArray().Single(value => value.GetProperty("key").GetString() == "defaults.strip_definition.symbol").GetProperty("value").GetString());
            Assert.Equal(0, resolved.Values.EnumerateArray().Single(value => value.GetProperty("key").GetString() == "defaults.strip_definition.variant").GetProperty("value").GetInt32());
            var interruption = Assert.Single(resolved.Values.EnumerateArray(), value => value.GetProperty("key").GetString() == "defaults.interruption.symbol");
            Assert.Equal("BP", interruption.GetProperty("value").GetString());
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            { File.SetAttributes(file, FileAttributes.Normal); }
            Directory.Delete(root, recursive: true);
        }
    }
}
