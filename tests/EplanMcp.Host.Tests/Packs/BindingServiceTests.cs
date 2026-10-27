using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Packs;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class BindingServiceTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-binding-").FullName);
    private readonly ManualTime _clock = new(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new()
    {
        Status = new([], new() { Eplan = new() { ProcessId = 42, Version = "2026.0.3" } }, null),
    };
    private readonly FakeApprovalChannel _channel = new("fake", ApprovalDecision.Granted);
    private PackLibrary Library => new(_locations, HostConfig.Defaults(_locations), Audit, _clock);
    private AuditLog Audit => new(_locations.Audit, _clock);
    private string Project => PlanSnapshotHashes.ProjectKey(Path.Combine(_locations.DefaultScratch, "Demo"));
    private string BindingFile => Path.Combine(Library.Workspaces.FolderOf(Project), ProjectBinding.FileName);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Write_TwoWritersWithSameRevision_OnlyOneCommitsWithCas()
    {
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new DeferredChannel(firstRead, continueFirst);
        WritePack("one");
        WritePack("two");
        ProjectBinding one = Proposal("one"), two = Proposal("two");
        Task<ProjectBinding> first = WriteAsync(one, 0, channel);
        await firstRead.Task.WaitAsync(Token);
        ProjectBinding winner = await WriteAsync(two, 0);
        continueFirst.SetResult();

        BridgeCallException conflict = await Assert.ThrowsAsync<BridgeCallException>(() => first);

        Assert.Equal(ErrorReason.PlanStale, conflict.Error.Reason);
        Assert.StartsWith("/revision:", conflict.Error.Message, StringComparison.Ordinal);
        Assert.Equal(1, winner.Revision);
        Assert.Equal("two", Assert.Single(winner.Locks).Id);
        Assert.Equal(PackJson.Serialize(winner), File.ReadAllBytes(BindingFile));
        Assert.Single(Records(), r => r["phase"]!.GetValue<string>() == "committed");
    }

    [Fact]
    public async Task Write_ConcurrentLocalChanges_SerializesBeforeCheckingRevision()
    {
        var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        BindingService firstService = Service(checkpoint: point =>
        {
            if (point == "staged")
            {
                staged.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(3), Token));
            }
        });
        Task<ProjectBinding> first = Task.Run(() => firstService.WriteAsync(Proposal(), 0, "first", "bind", null, Token), Token);
        await staged.Task.WaitAsync(Token);
        Task<ProjectBinding> second = WriteAsync(Proposal(), 0);
        Assert.False(second.IsCompleted);
        release.Set();
        Assert.Equal(1, (await first).Revision);
        Assert.Equal(ErrorReason.PlanStale, (await Assert.ThrowsAsync<BridgeCallException>(() => second)).Error.Reason);
    }

    [Theory]
    [InlineData("written")]
    [InlineData("staged")]
    public async Task Write_FailsDuringWriteOrBeforeReplace_PreservesOldFileAndCleansTemporary(string failure)
    {
        Seed(Proposal() with { Revision = 4, Cabinets = [Proposal().Cabinets[0] with { Title = "original" }] });
        byte[] old = File.ReadAllBytes(BindingFile);
        BindingService service = Service(checkpoint: point =>
        {
            if (point == failure)
            { throw new IOException("Injected write failure"); }
        });

        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(Proposal(), 4, "test", "bind", null, Token));

        Assert.Equal(old, File.ReadAllBytes(BindingFile));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(BindingFile)!, "*.partial-*"));
    }

    [Fact]
    public async Task Write_AtomicReplace_ReadersKeepTheOldSnapshotWhileNewReadersSeeCompleteNewFile()
    {
        Seed(Proposal() with { Revision = 4, Cabinets = [Proposal().Cabinets[0] with { Title = "original" }] });
        byte[] before = File.ReadAllBytes(BindingFile);
        using var reader = new FileStream(BindingFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        BindingService service = Service(checkpoint: point =>
        {
            if (point == "staged")
            {
                Assert.Equal(before, File.ReadAllBytes(BindingFile));
                string partial = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(BindingFile)!, "*.partial-*"));
                Assert.Equal(5, PackJson.ReadBinding(File.ReadAllBytes(partial)).Revision);
            }
        });

        Task<ProjectBinding> replacing = service.WriteAsync(Proposal(), 4, "test", "bind", null, Token);
        byte[] retained = new byte[reader.Length];
        await reader.ReadExactlyAsync(retained, Token);
        Assert.Equal(before, retained);
        reader.Dispose();
        ProjectBinding result = await replacing;
        Assert.Equal(PackJson.Serialize(result), File.ReadAllBytes(BindingFile));
        Assert.Equal(5, result.Revision);
    }

    [Fact]
    public async Task Write_ValuesAndMappingWithSameLocks_AuditsWithoutConfirmation()
    {
        WritePack("root");
        ProjectBinding first = await WriteAsync(Proposal("root"), 0);
        byte[] before = File.ReadAllBytes(BindingFile);
        ProjectBinding proposed = first with
        {
            ProjectValues = JsonNode.Parse("""{"motor.power":{"default":7.5}}""")!.AsObject(),
            Cabinets = [new() { Id = "a", Layers = [], Values = new(), Structure = JsonNode.Parse("""{"prefixes":[{"location":"+A1"}]}""")!.AsObject() }],
        };

        ProjectBinding result = await WriteAsync(proposed, 1);

        Assert.Single(_channel.Questions);
        JsonObject committed = Records().Last();
        Assert.Equal("workspace_change", committed["kind"]!.GetValue<string>());
        Assert.Equal("committed", committed["phase"]!.GetValue<string>());
        Assert.Equal(PlanStore.Hash(before), committed["beforeDigest"]!.GetValue<string>());
        Assert.Equal(PlanStore.Hash(File.ReadAllBytes(BindingFile)), committed["afterDigest"]!.GetValue<string>());
        Assert.Equal((Project, "test-source", 2), (committed["project"]!.GetValue<string>(), committed["source"]!.GetValue<string>(), committed["revision"]!.GetValue<int>()));
        Assert.Null(committed["decision"]);
        Assert.Equal(2, result.Revision);
        Assert.Equal((4, null), AuditVerifier.Verify(_locations.Audit));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task Write_ClosureConfirmation_ShowsEveryExactVersionAndDigest()
    {
        PackReference dependency = WritePack("dependency");
        PackReference root = WritePack("root", dependencies: [dependency]);

        await WriteAsync(Proposal("root"), 0);

        ApprovalQuestion question = Assert.Single(_channel.Questions);
        Assert.Equal(["dependency", "root"], question.Plan.Changes.Select(c => c.Name));
        Assert.Equal(new[] { dependency, root }.Select(p => $"{p.Version} sha256:{p.Digest}"), question.Plan.Changes.Select(c => c.NewValue));
        ApprovalAskArgs dialog = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(2, dialog.Changes.Count);
        Assert.All(dialog.Changes, line => Assert.DoesNotContain("…", line, StringComparison.Ordinal));
        Assert.Contains("local binding", dialog.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("first")]
    [InlineData("replace")]
    [InlineData("remove")]
    public async Task Write_LocksChange_ConfirmsWithoutHoldingEitherLock(string action)
    {
        WritePack("one");
        WritePack("two");
        ProjectBinding proposed = Proposal("one");
        int revision = 0;
        if (action != "first")
        {
            ProjectBinding original = await WriteAsync(proposed, 0);
            revision = original.Revision;
            proposed = Proposal(action == "replace" ? "two" : null);
        }
        var channel = new FakeApprovalChannel("existing-channel", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                using var projectLock = new FileStream(new PlanPaths(_locations.Root).ProjectLock(Project), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                Directory.CreateDirectory(Path.GetDirectoryName(BindingFile)!);
                using var bindingLock = new FileStream(Path.Combine(Path.GetDirectoryName(BindingFile)!, ".binding.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            },
        };

        ProjectBinding result = await WriteAsync(proposed, revision, channel);

        ApprovalQuestion question = Assert.Single(channel.Questions);
        Assert.Equal(PlanStore.Hash(PackJson.Serialize(result)), question.Plan.Fingerprint);
        Assert.NotEqual(question.Plan.Fingerprint, question.ArgumentsHash);
        Assert.Contains("pack locks", question.Plan.Changes[0].Property, StringComparison.Ordinal);
        Assert.Equal("existing-channel", Records().Last()["channel"]!.GetValue<string>());
        Assert.Equal("Granted", Records().Last()["decision"]!.GetValue<string>());
    }

    [Fact]
    public async Task Write_ConfirmationDeniedOrUnavailable_DoesNotPublish()
    {
        WritePack("root");
        var denied = new FakeApprovalChannel("fake", ApprovalDecision.Denied);
        Assert.Equal(ErrorReason.ApprovalDenied, (await Assert.ThrowsAsync<BridgeCallException>(() => WriteAsync(Proposal("root"), 0, denied))).Error.Reason);
        Assert.False(File.Exists(BindingFile));
        Assert.Equal("denied", Records().Last()["phase"]!.GetValue<string>());
        _bridge.Status = new([], null, null);
        Assert.Equal(ErrorReason.ApprovalDenied, (await Assert.ThrowsAsync<BridgeCallException>(() => WriteAsync(Proposal("root"), 0))).Error.Reason);
        Assert.False(File.Exists(BindingFile));
    }

    [Fact]
    public async Task Write_AuditUnavailable_PreservesOldFile()
    {
        Seed(Proposal() with { Revision = 4, Cabinets = [Proposal().Cabinets[0] with { Title = "original" }] });
        byte[] before = File.ReadAllBytes(BindingFile);
        Directory.CreateDirectory(_locations.Audit);
        File.WriteAllText(Path.Combine(_locations.Audit, "2026-10-27.jsonl"), "damaged\n");

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => WriteAsync(Proposal(), 4));

        Assert.Equal(ErrorReason.AuditUnavailable, error.Error.Reason);
        Assert.Equal(before, File.ReadAllBytes(BindingFile));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(BindingFile)!, "*.partial-*"));
    }

    [Fact]
    public async Task Write_FinalAuditUnavailable_ReportsThatReplacementAlreadyCommitted()
    {
        var audit = new AuditLog(_locations.Audit, new FailingCommitClock(Path.Combine(_locations.Audit, "2026-10-27.jsonl"), _clock.GetUtcNow()));
        var service = new BindingService(Library, audit, new ApprovalService([_channel], HostConfig.Defaults(_locations), _clock, NullLogger<ApprovalService>.Instance), _bridge);

        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => service.WriteAsync(Proposal(), 0, "test", "bind", null, Token));

        Assert.Equal(ErrorReason.AuditUnavailable, failure.Error.Reason);
        Assert.Contains("binding changed", failure.Error.Message, StringComparison.Ordinal);
        Assert.Equal(1, PackJson.ReadBinding(File.ReadAllBytes(BindingFile)).Revision);
    }

    [Fact]
    public async Task Write_V1ExplicitConversion_PreservesOriginalEvidence()
    {
        var old = new ProjectBinding { BindingVersion = 1, Project = Project, Revision = 6 };
        Seed(old);
        byte[] before = File.ReadAllBytes(BindingFile);

        ProjectBinding result = await WriteAsync(Proposal(), 6);

        Assert.Equal((2, 7), (result.BindingVersion, result.Revision));
        string evidence = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(BindingFile)!, "binding.v1-*.json"));
        Assert.Equal(before, File.ReadAllBytes(evidence));
        Assert.Equal(before, PackJson.Serialize(old));
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("manifest")]
    [InlineData("lock")]
    public async Task Write_ChangesDuringConfirmation_RejectsWithoutOverwriting(string tamper)
    {
        WritePack("root");
        Seed(Proposal());
        var channel = new FakeApprovalChannel("fake", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                if (tamper == "binding")
                {
                    File.AppendAllText(BindingFile, "\n");
                }
                else if (tamper == "manifest")
                {
                    string file = Path.Combine(_locations.Packs, "root", "1.0.0", PackManifest.FileName);
                    PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(file)) with { Name = "Changed" };
                    byte[] bytes = PackJson.Serialize(manifest);
                    File.WriteAllBytes(file, bytes);
                    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(file)!, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(new() { Id = "root", Version = "1.0.0", Digest = PackDigest.Compute(bytes) }, [])));
                }
                else
                {
                    File.Delete(Path.Combine(_locations.Packs, "root", "1.0.0", PackManifest.LockFileName));
                }
            },
        };

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => WriteAsync(Proposal("root"), 0, channel));

        Assert.Equal(tamper == "lock" ? ErrorReason.PackInvalid : ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal(0, PackJson.ReadBinding(File.ReadAllBytes(BindingFile)).Revision);
        Assert.Empty(Records());
    }

    [Fact]
    public async Task Write_IncompatibleRootVersions_ReturnsConfigConflict()
    {
        PackReference first = WritePack("shared");
        WritePack("shared", "2.0.0");
        WritePack("root", dependencies: [first]);
        ProjectBinding proposed = Proposal("root") with { PackRoots = [new() { Id = "root", Version = "1.0.0" }, new() { Id = "shared", Version = "2.0.0" }] };
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => WriteAsync(proposed, 0));
        Assert.Equal(ErrorReason.ConfigConflict, error.Error.Reason);
        Assert.StartsWith("/packRoots/1:", error.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_channel.Questions);
        Assert.False(File.Exists(BindingFile));
    }

    private BindingService Service(IApprovalChannel? channel = null, Action<string>? checkpoint = null) => new(Library, Audit,
        new ApprovalService([channel ?? _channel], HostConfig.Defaults(_locations), _clock, NullLogger<ApprovalService>.Instance), _bridge)
    { Checkpoint = checkpoint };

    private Task<ProjectBinding> WriteAsync(ProjectBinding proposal, int revision, IApprovalChannel? channel = null) =>
        Service(channel).WriteAsync(proposal, revision, "test-source", "bind", null, Token);

    private ProjectBinding Proposal(string? root = null) => new()
    {
        BindingVersion = 2,
        Project = Project,
        Revision = 0,
        ProjectValues = new(),
        PackRoots = root is null ? [] : [new() { Id = root, Version = "1.0.0" }],
        Cabinets = [new() { Id = "default", Layers = [], Values = new(), Structure = JsonNode.Parse("""{"all":true}""")!.AsObject() }],
    };

    private void Seed(ProjectBinding binding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BindingFile)!);
        File.WriteAllBytes(BindingFile, PackJson.Serialize(binding));
    }

    private PackReference WritePack(string id, string version = "1.0.0", PackReference[]? dependencies = null)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_locations.Packs, id, version)).FullName;
        var manifest = new PackManifest
        {
            SchemaVersion = 2,
            Id = id,
            Name = id,
            Version = version,
            Compatibility = new(),
            Resources = [],
            Dependencies = [.. (dependencies ?? []).Select(p => new PackDependency { Id = p.Id, Version = p.Version })],
            Provenance = new() { CreatedAt = _clock.GetUtcNow(), CreatedBy = "eplanmcp test", Inputs = [] },
        };
        byte[] bytes = PackJson.Serialize(manifest);
        var pin = new PackReference { Id = id, Version = version, Digest = PackDigest.Compute(bytes) };
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), bytes);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(pin, dependencies ?? [])));
        return pin;
    }

    private List<JsonObject> Records() => Directory.Exists(_locations.Audit)
        ? [.. Directory.GetFiles(_locations.Audit, "*.jsonl").SelectMany(File.ReadLines).Select(line => JsonNode.Parse(line)!.AsObject())] : [];

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    private sealed class FailingCommitClock(string auditFile, DateTimeOffset now) : TimeProvider
    {
        private int _calls;
        public override DateTimeOffset GetUtcNow()
        {
            if (++_calls == 2)
            {
                File.WriteAllText(auditFile, "damaged\n");
            }
            return now;
        }
    }

    private sealed class DeferredChannel(TaskCompletionSource entered, TaskCompletionSource release) : IApprovalChannel
    {
        public string Name => "fake";
        public string Where => "in EPLAN";
        public async Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
        {
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, ApprovalDecision.Granted);
        }
    }
}
