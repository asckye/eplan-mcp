using System.Diagnostics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class ReferenceCopyTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-reference-").FullName;
    private readonly FakeBridgeClient _bridge = new();
    private readonly Lifecycle _lifecycle = new();
    private readonly ReferenceCopies _copies;
    private readonly ReferenceCopyService _service;
    private string Scratch => Path.Combine(_home, "scratch");
    private string Source => Path.Combine(_home, "source", "Sample.elk");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ReferenceCopyTests()
    {
        WriteProject(Source);
        _copies = new ReferenceCopies(_lifecycle, _lifecycle, _lifecycle);
        _bridge.Handlers[OperationNames.ReferencePrepare] = args => new ReferencePrepareOperation(_copies).Execute((ReferencePrepareArgs)args!)!;
        _bridge.Handlers[OperationNames.ReferenceClose] = args => new ReferenceCloseOperation(_copies).Execute((ReferenceCloseArgs)args!)!;
        _service = Service();
    }

    [Fact]
    public async Task PrepareAndClose_UsesFreshEmptyFolderAndClosesOnlyItsDatabase()
    {
        PackResource resource = await ResourceAsync();
        _lifecycle.Projects.Add(new OpenProject { Path = Path.Combine(_home, "Engineer"), Current = true });
        ReferenceCopyData copy = await _service.PrepareAsync(resource, Token);
        ReferenceCopyData second = await _service.PrepareAsync(resource, Token);
        Assert.NotEqual(copy.Path, second.Path);
        Assert.StartsWith(Scratch + Path.DirectorySeparatorChar, copy.Path, StringComparison.Ordinal);
        Assert.Equal(("18.0", _lifecycle.Projects[0].Path, _lifecycle.Projects[0].Path), (copy.DatabaseVersion, copy.CurrentBefore, copy.CurrentAfter));
        Assert.All(_lifecycle.Projects.Skip(1), p => Assert.True(p.ReadOnly));

        ProjectListData closed = await _service.CloseAsync(copy.Handle, Token);

        Assert.Equal([copy.Path], _lifecycle.Closed);
        Assert.Equal(2, closed.Projects.Count);
        Assert.Contains(closed.Projects, p => p.Path == second.Path && p.ReadOnly);
        Assert.Equal(resource.Sha256, (await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(Source), Token)).Sha256);
        await _service.CloseAsync(second.Handle, Token);
        Assert.Single(_lifecycle.Projects);
    }

    [Theory]
    [InlineData("forged")]
    [InlineData("other-service")]
    [InlineData("consumed")]
    public async Task Close_WithoutServiceOwnership_NeverCallsBridge(string condition)
    {
        ReferenceCopyData copy = await _service.PrepareAsync(await ResourceAsync(), Token);
        ReferenceCopyService caller = condition == "other-service" ? Service() : _service;
        if (condition == "consumed")
        { await _service.CloseAsync(copy.Handle, Token); }
        int calls = _bridge.Calls.Count;
        await Assert.ThrowsAsync<BridgeCallException>(() => caller.CloseAsync(condition == "forged" ? "forged" : copy.Handle, Token));
        Assert.Equal(calls, _bridge.Calls.Count);
    }

    [Fact]
    public async Task Extract_GapOnlyCanBeReplaced_ReaderSeesOnlyThePreparedCopyAndExplicitPages()
    {
        ReferenceCopyData copy = await ReadyAsync();
        var pages = new[] { "=AP+A1/1" };
        string copyHash = (await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(copy.Path), Token)).Sha256;
        ReferenceExtractionResult gap = await _service.ExtractAsync(copy.Handle, pages, new GapOnlyReferenceExtractor(), Token);
        Assert.Empty(gap.Facts);
        Assert.Equal(ErrorReason.CapabilityMissing, gap.Problem!.Reason);
        Assert.Equal("capability_missing", Assert.Single(gap.Gaps).Code);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectSnapshot);

        var reader = new Reader(_bridge);
        _bridge.Handlers[OperationNames.ProjectSnapshot] = args =>
        {
            var sent = Assert.IsType<ProjectSnapshotArgs>(args);
            Assert.Equal(copy.Path, sent.Project);
            Assert.Equal(pages, sent.Pages);
            Assert.False(sent.OpenReadOnly);
            return new ProjectSnapshotData { Format = "fake_snapshot" };
        };
        ReferenceExtractionResult result = await _service.ExtractAsync(copy.Handle, pages, reader, Token);

        Assert.Null(result.Problem);
        Assert.Empty(result.Gaps);
        Assert.Equal("fake_snapshot", Assert.Single(result.Facts).Value);
        Assert.Equal(2, _bridge.StatusRefreshes);
        Assert.Equal(copyHash, (await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(copy.Path), Token)).Sha256);
        Assert.Empty(_bridge.Writes);
        await _service.CloseAsync(copy.Handle, Token);
    }

    [Theory]
    [InlineData("licence_missing", "licence:APIExtension")]
    [InlineData("licence_unknown", "licence:APIExtension")]
    [InlineData("disconnected", "licence:APIExtension")]
    [InlineData("operation_missing", "op:project.snapshot")]
    [InlineData("operations_unknown", "op:project.snapshot")]
    public async Task Extract_CapabilityLostAfterPreparation_ReturnsGapAndHintWithoutCallingReader(string condition, string missing)
    {
        ReferenceCopyData copy = await ReadyAsync();
        if (condition == "disconnected")
        { _bridge.Status = new([], null, new BridgeError { Reason = ErrorReason.EplanNotConnected }); }
        else
        {
            HandshakeResult handshake = _bridge.Status.Handshake!;
            if (condition.StartsWith("licence", StringComparison.Ordinal))
            { handshake.Eplan.ApiExtensionLicence = condition == "licence_missing" ? false : null; }
            else
            { handshake.Operations = condition == "operations_unknown" ? null : []; }
        }
        var reader = new Reader(_bridge);
        int calls = _bridge.Calls.Count;

        ReferenceExtractionResult result = await _service.ExtractAsync(copy.Handle, ["=AP+A1/1"], reader, Token);

        Assert.Empty(result.Facts);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Problem!.Reason);
        Assert.Equal(missing, result.Problem.Metadata!["missing"]);
        Assert.NotEmpty(result.Problem.Message);
        Assert.Equal("references/Sample.elk", Assert.Single(result.Gaps).Subject);
        Assert.False(reader.Called);
        Assert.Equal(calls, _bridge.Calls.Count);
        Assert.Empty(_bridge.Writes);
        await _service.CloseAsync(copy.Handle, Token);
    }

    [Theory]
    [InlineData("forged")]
    [InlineData("closed")]
    [InlineData("other-service")]
    [InlineData("writable")]
    [InlineData("not-open")]
    public async Task Extract_WithoutOwnedReadOnlyCopy_RefusesBeforeReading(string condition)
    {
        ReferenceCopyData copy = await ReadyAsync();
        ReferenceCopyService caller = condition == "other-service" ? Service() : _service;
        if (condition == "closed")
        { await _service.CloseAsync(copy.Handle, Token); }
        if (condition == "writable")
        { _bridge.Projects[0].ReadOnly = false; }
        if (condition == "not-open")
        { _bridge.Projects.Clear(); }
        var reader = new Reader(_bridge);
        await Assert.ThrowsAsync<BridgeCallException>(() => caller.ExtractAsync(condition == "forged" ? "forged" : copy.Handle, ["=AP+A1/1"], reader, Token));
        Assert.False(reader.Called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extract_SourceChangedBeforeOrDuringReading_RejectsEvidence(bool during)
    {
        ReferenceCopyData copy = await ReadyAsync();
        var reader = new Reader(_bridge);
        void Tamper() => File.AppendAllText(Source, "changed");
        if (!during)
        { Tamper(); }
        _bridge.Handlers[OperationNames.ProjectSnapshot] = _ =>
        {
            Tamper();
            return new ProjectSnapshotData();
        };
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => _service.ExtractAsync(copy.Handle, ["=AP+A1/1"], reader, Token));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal(during, reader.Called);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("blank")]
    [InlineData("limit")]
    public async Task Extract_InvalidExplicitScope_RejectsWithPointer(string condition)
    {
        ReferenceCopyData copy = await ReadyAsync();
        string[] pages = condition switch { "empty" => [], "duplicate" => ["1", "1"], "blank" => [" "], _ => Enumerable.Range(1, 51).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray() };
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => _service.ExtractAsync(copy.Handle, pages, new Reader(_bridge), Token));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal("/scope/pages", error.Error.Metadata!["pointer"]);
        Assert.Equal(0, _bridge.StatusRefreshes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extract_ReaderFailsOrCancels_StillChecksSourceHash(bool cancel)
    {
        ReferenceCopyData copy = await ReadyAsync();
        _bridge.Handlers[OperationNames.ProjectSnapshot] = _ =>
        {
            File.AppendAllText(Source, "changed");
            throw cancel ? new OperationCanceledException() : new IOException("Reader failed.");
        };
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => _service.ExtractAsync(copy.Handle, ["=AP+A1/1"], new Reader(_bridge), Token));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
    }

    private async Task<ReferenceCopyData> ReadyAsync()
    {
        ReferenceCopyData copy = await _service.PrepareAsync(await ResourceAsync(), Token);
        _bridge.Projects.AddRange(_lifecycle.Projects);
        _bridge.Status = new([], new HandshakeResult { Eplan = new() { ApiExtensionLicence = true }, Operations = [OperationNames.ProjectSnapshot] }, null);
        return copy;
    }

    private sealed class Reader(FakeBridgeClient bridge) : IReferenceExtractor
    {
        public bool Called { get; private set; }
        public IReadOnlyList<string> RequiredOperations => [OperationNames.ProjectSnapshot];
        public async Task<ReferenceExtractionResult> ExtractAsync(string project, string resource, IReadOnlyList<string> pages, CancellationToken cancellationToken)
        {
            Called = true;
            ProjectSnapshotData snapshot = await bridge.InvokeAsync<ProjectSnapshotArgs, ProjectSnapshotData>(OperationNames.ProjectSnapshot, new() { Project = project, Pages = [.. pages] }, cancellationToken);
            return new([new PackFact { Resource = resource, Name = "format", Value = snapshot.Format, Evidence = "fake Bridge snapshot" }], []);
        }
    }

    [Fact]
    public async Task Close_OtherBridgeOwnerOrReopenedDatabase_Refuses()
    {
        ReferenceCopyData copy = await _service.PrepareAsync(await ResourceAsync(), Token);
        var other = new ReferenceCopies(_lifecycle, _lifecycle, _lifecycle);
        Assert.Throws<BridgeErrorException>(() => other.Close(new ReferenceCloseArgs { Handle = copy.Handle }));
        _lifecycle.Database++;
        await Assert.ThrowsAsync<BridgeErrorException>(() => _service.CloseAsync(copy.Handle, Token));
        Assert.Empty(_lifecycle.Closed);
        Assert.Contains(_lifecycle.Projects, p => p.Path == copy.Path);
    }

    [Theory]
    [InlineData("nonempty")]
    [InlineData("file")]
    [InlineData("absent")]
    public void Prepare_DestinationIsNotAnExistingEmptyFolder_RefusesBeforeCopy(string condition)
    {
        ReferencePrepareArgs args = Args();
        string folder = Path.GetDirectoryName(args.Destination)!;
        if (condition == "nonempty")
        { File.WriteAllText(Path.Combine(folder, "keep"), "existing"); }
        else if (condition == "file")
        { Directory.Delete(folder); File.WriteAllText(folder, "existing"); }
        else
        { Directory.Delete(folder); }
        Assert.Throws<BridgeErrorException>(() => _copies.Prepare(args));
        Assert.Equal(0, _lifecycle.Copies);
        Assert.Empty(_lifecycle.Closed);
    }

    [Fact]
    public async Task Prepare_FolderFilledAfterHostCheck_IsRejectedByBridge()
    {
        _bridge.Handlers[OperationNames.ReferencePrepare] = input =>
        {
            var args = (ReferencePrepareArgs)input!;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(args.Destination)!, "keep"), "existing");
            return _copies.Prepare(args);
        };
        PackResource resource = await ResourceAsync();
        await Assert.ThrowsAsync<BridgeErrorException>(() => _service.PrepareAsync(resource, Token));
        Assert.Equal(0, _lifecycle.Copies);
    }

    [Theory]
    [InlineData("source-open")]
    [InlineData("source-newer")]
    [InlineData("copy-newer")]
    [InlineData("source-session")]
    [InlineData("copy-session")]
    [InlineData("source-lock")]
    [InlineData("source-sublibrary-session")]
    public void Prepare_UnsafeSourceOrCopy_NeverOpensAnything(string condition)
    {
        ReferencePrepareArgs args = Args();
        if (condition == "source-open")
        { _lifecycle.Projects.Add(new OpenProject { Path = Source[..^4], Current = true }); }
        if (condition == "source-newer")
        { _lifecycle.BadVersion = Source; }
        if (condition == "copy-newer")
        { _lifecycle.BadVersion = args.Destination; }
        if (condition == "source-session")
        { File.Delete(Path.Combine(Path.ChangeExtension(Source, ".edb"), "User.eox")); }
        if (condition == "copy-session")
        { _lifecycle.AfterCopy = path => File.Delete(Path.Combine(Path.ChangeExtension(path, ".edb"), "User.eod")); }
        if (condition == "source-lock")
        { File.WriteAllText(Path.Combine(Path.ChangeExtension(Source, ".edb"), "project.lck"), "open elsewhere"); }
        if (condition == "source-sublibrary-session")
        { Directory.CreateDirectory(Path.Combine(Path.ChangeExtension(Source, ".edb"), "Symbols.sdb")); }
        Assert.Throws<BridgeErrorException>(() => _copies.Prepare(args));
        Assert.Equal(0, _lifecycle.Opens);
        Assert.Empty(_lifecycle.Closed);
        Assert.Equal(condition.StartsWith("copy", StringComparison.Ordinal) ? 1 : 0, _lifecycle.Copies);
    }

    [Fact]
    public async Task Prepare_ForwardSlashRoots_ProducesCanonicalPathsForExactClose()
    {
        var service = new ReferenceCopyService(_bridge, Scratch.Replace('\\', '/'), new Dictionary<string, string> { ["samples"] = Path.GetDirectoryName(Source)!.Replace('\\', '/') });
        ReferenceCopyData copy = await service.PrepareAsync(await ResourceAsync(), Token);
        Assert.DoesNotContain('/', copy.Path);
        Assert.Equal(Source, _lifecycle.LastSource);
        await service.CloseAsync(copy.Handle, Token);
        Assert.Equal([copy.Path], _lifecycle.Closed);
    }

    [Fact]
    public async Task Close_BridgeFailure_KeepsOwnershipForAnExplicitRetry()
    {
        ReferenceCopyData copy = await _service.PrepareAsync(await ResourceAsync(), Token);
        _bridge.Failure = new BridgeError { Reason = ErrorReason.EplanBusy, Message = "Busy" };
        await Assert.ThrowsAsync<BridgeCallException>(() => _service.CloseAsync(copy.Handle, Token));
        Assert.Empty(_lifecycle.Closed);
        _bridge.Failure = null;
        await _service.CloseAsync(copy.Handle, Token);
        Assert.Equal([copy.Path], _lifecycle.Closed);
    }

    [Fact]
    public async Task Prepare_SourceRootsAreCaptured_BeforeTheCallersMapChanges()
    {
        var roots = new Dictionary<string, string> { ["samples"] = Path.GetDirectoryName(Source)! };
        var service = new ReferenceCopyService(_bridge, Scratch, roots);
        roots["samples"] = Path.Combine(_home, "untrusted-replacement");
        ReferenceCopyData copy = await service.PrepareAsync(await ResourceAsync(), Token);
        Assert.Equal(Source, _lifecycle.LastSource);
        await service.CloseAsync(copy.Handle, Token);
    }

    [Fact]
    public async Task Prepare_SourceHashChangedBeforeUse_DoesNotCallBridge()
    {
        PackResource resource = await ResourceAsync();
        File.AppendAllText(Source, "changed");
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => _service.PrepareAsync(resource, Token));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Empty(_bridge.Calls);
    }

    [Fact]
    public async Task Prepare_ScratchInsideSourceDatabase_DoesNotCreateAnyFolder()
    {
        string data = Path.ChangeExtension(Source, ".edb");
        PackResource resource = await ResourceAsync();
        var service = new ReferenceCopyService(_bridge, data, new Dictionary<string, string> { ["samples"] = Path.GetDirectoryName(Source)! });
        await Assert.ThrowsAsync<BridgeCallException>(() => service.PrepareAsync(resource, Token));
        Assert.Empty(Directory.EnumerateDirectories(data));
        Assert.Empty(_bridge.Calls);
    }

    [Fact]
    public async Task Prepare_SourceChangesDuringCopy_ClosesTheCreatedCopyAndRefuses()
    {
        PackResource resource = await ResourceAsync();
        _lifecycle.AfterCopy = _ => File.AppendAllText(Source, "changed");
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => _service.PrepareAsync(resource, Token));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Single(_lifecycle.Closed);
        Assert.Empty(_lifecycle.Projects);
    }

    [Fact]
    public async Task Close_SourceChangesBeforeClose_StillClosesOnlyCopyAndReportsStale()
    {
        PackResource resource = await ResourceAsync();
        ReferenceCopyData copy = await _service.PrepareAsync(resource, Token);
        File.AppendAllText(Source, "changed");
        Assert.Equal(ErrorReason.PlanStale, (await Assert.ThrowsAsync<BridgeCallException>(() => _service.CloseAsync(copy.Handle, Token))).Error.Reason);
        Assert.Equal([copy.Path], _lifecycle.Closed);
    }

    [Fact]
    public async Task Prepare_Backup_UsesTheSameEmptyFolderAndCloseBoundary()
    {
        string archive = Path.ChangeExtension(Source, ".zw1");
        File.WriteAllText(archive, "fake native archive");
        (string sha256, long bytes) = await PackHasher.HashFileAsync(archive, Token);
        PackResource resource = ReferenceSourceTests.Resource(ResourceKind.ProjectBackup, "Sample.zw1") with { Sha256 = sha256, Bytes = bytes };
        ReferenceCopyData copy = await _service.PrepareAsync(resource, Token);
        Assert.EndsWith(".zw1", _lifecycle.LastSource, StringComparison.Ordinal);
        await _service.CloseAsync(copy.Handle, Token);
        Assert.Equal(sha256, (await PackHasher.HashFileAsync(archive, Token)).Sha256);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("scratch")]
    [InlineData("source-tree")]
    [InlineData("copy-tree")]
    public async Task Prepare_JunctionInAnyBoundary_IsRejected(string condition)
    {
        PackResource resource = await ResourceAsync();
        string outside = Path.Combine(_home, "outside");
        Directory.CreateDirectory(outside);
        if (condition == "source")
        {
            string alias = Path.Combine(_home, "alias");
            Junction(alias, Path.GetDirectoryName(Source)!);
            ReferenceCopyService service = new(_bridge, Scratch, new Dictionary<string, string> { ["samples"] = alias });
            await Assert.ThrowsAsync<IOException>(() => service.PrepareAsync(resource, Token));
            Directory.Delete(alias);
        }
        else if (condition == "scratch")
        {
            Junction(Scratch, outside);
            await Assert.ThrowsAsync<IOException>(() => _service.PrepareAsync(resource, Token));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
            Directory.Delete(Scratch);
        }
        else
        {
            string? alias = null;
            if (condition == "source-tree")
            {
                alias = Path.Combine(Path.ChangeExtension(Source, ".edb"), "alias");
                Junction(alias, outside);
            }
            else
            {
                _lifecycle.AfterCopy = path => { alias = Path.Combine(Path.ChangeExtension(path, ".edb"), "alias"); Junction(alias, outside); };
            }
            if (condition == "source-tree")
            { await Assert.ThrowsAsync<BridgeCallException>(() => _service.PrepareAsync(resource, Token)); }
            else
            { await Assert.ThrowsAsync<BridgeErrorException>(() => _service.PrepareAsync(resource, Token)); }
            Assert.NotNull(alias);
            Directory.Delete(alias);
        }
        Assert.Equal(0, _lifecycle.Opens);
    }

    private ReferenceCopyService Service() => new(_bridge, Scratch, new Dictionary<string, string> { ["samples"] = Path.GetDirectoryName(Source)! });
    private async Task<PackResource> ResourceAsync()
    {
        (string hash, long bytes, int count) = await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(Source), Token);
        return ReferenceSourceTests.Resource(ResourceKind.Project, "Sample.elk") with { Sha256 = hash, Bytes = bytes, Files = count };
    }
    private ReferencePrepareArgs Args()
    {
        string folder = Path.Combine(Scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return new() { Source = Source, Destination = Path.Combine(folder, "Reference.elk"), ScratchRoot = Scratch };
    }
    private static void WriteProject(string link)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.WriteAllText(link, "project link");
        string data = Path.ChangeExtension(link, ".edb");
        Directory.CreateDirectory(data);
        foreach (string name in new[] { "User.eod", "User.eox", "Pages.eod" })
        { File.WriteAllText(Path.Combine(data, name), name); }
    }
    private static void Junction(string path, string target)
    {
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
    public void Dispose() => Directory.Delete(_home, recursive: true);

    private sealed class Lifecycle : IProjectCatalog, IProjectFiles, IReferenceProjects
    {
        public List<OpenProject> Projects { get; } = [];
        public List<string> Closed { get; } = [];
        public uint Database { get; set; } = 42;
        public int Copies { get; private set; }
        public int Opens { get; private set; }
        public string? BadVersion { get; set; }
        public string? LastSource { get; private set; }
        public Action<string>? AfterCopy { get; set; }
        private readonly Dictionary<string, uint> _databases = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<OpenProject> ListOpenProjects() => [.. Projects];
        public (string Status, string DatabaseVersion) Compatibility(string linkFile) => (linkFile == BadVersion ? "IsNewer" : "IsCompatible", "18.0");
        public void Copy(string source, string destination)
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(destination)!));
            Copies++;
            LastSource = source;
            WriteProject(destination);
            AfterCopy?.Invoke(destination);
        }
        public uint OpenReadOnly(string path)
        {
            Opens++;
            Projects.Add(new OpenProject { Path = path, ReadOnly = true, Current = Projects.Count == 0 });
            _databases.Add(path, Database);
            return Database;
        }
        public void Close(string path, uint database)
        {
            if (database != Database || _databases[path] != database)
            { throw new BridgeErrorException(ErrorReason.InvalidArgument, "Reopened project belongs to another owner."); }
            Closed.Add(path);
            Projects.RemoveAll(p => p.Path == path);
        }
    }
}
