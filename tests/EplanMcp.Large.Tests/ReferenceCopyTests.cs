using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;

namespace EplanMcp.Large.Tests;

/// <summary>Native reference copy/restore; only the sample-derived source and the service's scratch copy are involved.</summary>
[Trait("Size", "Large")]
public sealed class ReferenceCopyTests(LiveEplan eplan)
{
    private static readonly string[] _sessionFiles = ["User.eod", "User.eox"];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sample_PrepareExtractReadOnlyAndClose_LeavesSourceHashAndOtherOpenProjectsUnchanged(bool backup)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        BridgeStatus status = await bridge.GetStatusAsync(token);
        Assert.SkipUnless(status.Handshake?.Operations is { } operations && operations.Contains(OperationNames.ReferencePrepare) && operations.Contains(OperationNames.ReferenceClose),
            "M0a operations are deliberately unregistered; run after the reference integration enables them.");
        string root = Path.Combine(LiveEplan.ScratchRoot(), "reference-test-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source", "Sample.elk");
        string? archive = Environment.GetEnvironmentVariable("EPLANMCP_LARGE_ZW1");
        Assert.SkipWhen(backup && (archive is null || !File.Exists(archive)), "Set EPLANMCP_LARGE_ZW1 to a non-customer sample project backup for native restore coverage.");
        if (backup)
        {
            source = Path.ChangeExtension(source, ".zw1");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.Copy(archive!, source);
        }
        else
        { OfflineHost.CopyFixture(LiveEplan.ScratchRoot(), source[..^4]); }
        (string hash, long bytes, int count) = backup ? await HashArchiveAsync(source, token) : await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(source), token);
        var resource = new PackResource
        {
            Path = "references/" + Path.GetFileName(source),
            Kind = backup ? ResourceKind.ProjectBackup : ResourceKind.Project,
            Storage = ResourceStorage.Referenced,
            Reference = new ResourceReference { Root = "sample", Path = Path.GetFileName(source) },
            Sha256 = hash,
            Bytes = bytes,
            Files = count
        };
        var service = new ReferenceCopyService(bridge, root, new Dictionary<string, string> { ["sample"] = Path.GetDirectoryName(source)! });
        ProjectListData before = await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), token);
        ReferenceCopyData? copy = null;
        try
        {
            copy = await service.PrepareAsync(resource, token);
            Assert.Equal("18.0", copy.DatabaseVersion);
            Assert.Equal(before.Projects.FirstOrDefault(p => p.Current)?.Path, copy.CurrentBefore);
            ProjectListData open = await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList, new(), token);
            OpenProject prepared = Assert.Single(open.Projects, p => Same(p.Path, copy.Path));
            Assert.True(prepared.ReadOnly);
            Assert.Equal(open.Projects.FirstOrDefault(p => p.Current)?.Path, copy.CurrentAfter);
            Assert.DoesNotContain(open.Projects, p => Same(p.Path, source));
            Assert.All(_sessionFiles, name => Assert.True(File.Exists(Path.Combine(Path.ChangeExtension(copy.Path, ".edb"), name))));
            LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = copy.Path });
            Assert.NotEmpty(snapshot.Pages);
            string[] pages = [snapshot.Pages[0].Name];
            Dictionary<string, string> copyFiles = LiveSnapshots.DesignFiles(copy.Path[..^4]);
            ReferenceExtractionResult gap = await service.ExtractAsync(copy.Handle, pages, new GapOnlyReferenceExtractor(), token);
            Assert.Empty(gap.Facts);
            Assert.Equal(ErrorReason.CapabilityMissing, gap.Problem!.Reason);
            ReferenceExtractionResult extracted = await service.ExtractAsync(copy.Handle, pages, new SnapshotExtractor(bridge), token);
            Assert.Null(extracted.Problem);
            Assert.Equal(snapshot.Pages[0].Hash, Assert.Single(extracted.Facts).Value);
            Assert.Equal(copyFiles.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(copy.Path[..^4]).OrderBy(p => p.Key, StringComparer.Ordinal));
            Assert.True((await bridge.GetStatusAsync(true, token)).Handshake!.Eplan.ApiExtensionLicence);
        }
        finally
        {
            if (copy is not null)
            {
                ProjectListData closed = await service.CloseAsync(copy.Handle, CancellationToken.None);
                Assert.Equal(before.Projects.Select(p => p.Path).Order(StringComparer.OrdinalIgnoreCase), closed.Projects.Select(p => p.Path).Order(StringComparer.OrdinalIgnoreCase));
                await Assert.ThrowsAsync<BridgeCallException>(() => service.CloseAsync(copy.Handle, token));
            }
        }
        Assert.Null(await PackVerifier.ReferenceProblemAsync(resource, new Dictionary<string, string> { ["sample"] = Path.GetDirectoryName(source)! }, token));
        ProjectCreateTests.Delete(root);
    }

    private sealed class SnapshotExtractor(BridgeClient bridge) : IReferenceExtractor
    {
        public IReadOnlyList<string> RequiredOperations => [OperationNames.ProjectSnapshot];
        public async Task<ReferenceExtractionResult> ExtractAsync(string project, string resource, IReadOnlyList<string> pages, CancellationToken cancellationToken)
        {
            ProjectSnapshotData snapshot = await bridge.InvokeAsync<ProjectSnapshotArgs, ProjectSnapshotData>(OperationNames.ProjectSnapshot, new() { Project = project, Pages = [.. pages] }, cancellationToken);
            Assert.True(snapshot.Complete);
            SnapshotPage page = Assert.Single(snapshot.Pages);
            return new([new PackFact { Resource = resource, Name = "page_hash", Value = page.Hash, Evidence = page.Name }], []);
        }
    }

    private static bool Same(string path, string link) => string.Equals(path.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) ? path : path + ".elk", link, StringComparison.OrdinalIgnoreCase);
    private static async Task<(string, long, int)> HashArchiveAsync(string path, CancellationToken cancellationToken)
    {
        (string hash, long bytes) = await PackHasher.HashFileAsync(path, cancellationToken);
        return (hash, bytes, 1);
    }
}
