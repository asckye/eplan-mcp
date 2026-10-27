using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class KnowledgeReviewServiceTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-review-").FullName);
    private readonly ManualTime _clock = new(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeApprovalChannel _channel = new("engineer", ApprovalDecision.Granted);
    private readonly FakeBridgeClient _bridge = new()
    {
        Status = new([], new() { Eplan = new() { ProcessId = 42, Version = "2026.0.3" } }, null),
    };
    private AuditLog Audit => new(_locations.Audit, _clock) { LockWait = TimeSpan.Zero };
    private PackLibrary Library => new(_locations, HostConfig.Defaults(_locations), Audit, _clock);
    private string Folder => Library.Dev.FolderOf("acme.panels");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    [InlineData("close_gap")]
    [InlineData("promote")]
    public async Task Review_Confirmed_RecordsOnlyInLocalAuditAndSurvivesServiceRestart(string decision)
    {
        KnowledgeReview review = Seed(decision == "close_gap" ? KnowledgeKind.Gap : KnowledgeKind.Candidate, decision);
        Dictionary<string, byte[]> before = FilesOfPack();

        string digest = await ConfirmAsync(review);

        Assert.True(await Service().VerifyAsync(digest, review, Token));
        Assert.False(await Service().VerifyAsync(review.ContentDigest, review, Token));
        JsonObject record = Assert.Single(await Audit.ReadAsync(Token));
        Assert.Equal(("knowledge_review", "Granted", "engineer", decision), ((string?)record["kind"], (string?)record["confirmation"], (string?)record["channel"], (string?)record["decision"]));
        Assert.Equal(review.Resource, (string?)record["resource"]);
        Assert.Equal(review.ContentDigest, (string?)record["contentDigest"]);
        Assert.Equal(review.TargetResource, (string?)record["targetResource"]);
        Assert.Equal((1, null), AuditVerifier.Verify(_locations.Audit));
        Assert.All(before, file => Assert.Equal(file.Value, File.ReadAllBytes(file.Key)));
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), FilesOfPack().Keys.Order(StringComparer.Ordinal));
        Assert.Empty(_bridge.Writes);
        ApprovalQuestion question = Assert.Single(_channel.Questions);
        Assert.Contains(question.Plan.Changes, c => c.OldValue == review.ContentDigest);
        Assert.Contains(question.Plan.Changes, c => c.NewValue == ReviewFixtures.Guide.Resource.Sha256);
        Assert.Contains("local knowledge review", ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US")).Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Denied", ErrorReason.ApprovalDenied)]
    [InlineData("Cancelled", ErrorReason.ApprovalDenied)]
    [InlineData("Expired", ErrorReason.ApprovalExpired)]
    [InlineData("Unavailable", ErrorReason.ApprovalDenied)]
    public async Task Review_ConfirmationDenied_RecordsNoDecision(string decision, string reason)
    {
        KnowledgeReview review = Seed();
        var channel = new FakeApprovalChannel("engineer", Enum.Parse<ApprovalDecision>(decision));

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => ConfirmAsync(review, channel));

        Assert.Equal(reason, error.Error.Reason);
        Assert.Empty(await Audit.ReadAsync(Token));
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("arguments")]
    [InlineData("request")]
    public async Task Review_MismatchedAnswer_DoesNotRecordConfirmation(string mismatch)
    {
        KnowledgeReview review = Seed();
        var channel = new FakeApprovalChannel("engineer", ApprovalDecision.Granted)
        {
            Bind = answer => mismatch switch
            {
                "fingerprint" => answer with { Fingerprint = "another" },
                "arguments" => answer with { ArgumentsHash = "another" },
                _ => answer with { RequestId = "another" },
            },
        };

        Assert.Equal(ErrorReason.ApprovalDenied, (await Assert.ThrowsAsync<BridgeCallException>(() => ConfirmAsync(review, channel))).Error.Reason);
        Assert.Empty(await Audit.ReadAsync(Token));
    }

    [Fact]
    public async Task Review_NoChannelOrOffline_RecordsNoDecision()
    {
        KnowledgeReview review = Seed();
        _bridge.Status = new([], null, null);
        Assert.Equal(ErrorReason.ApprovalDenied, (await Assert.ThrowsAsync<BridgeCallException>(() => ConfirmAsync(review))).Error.Reason);
        _bridge.Status = new([], new() { Eplan = new() { ProcessId = 42, Version = "2026.0.3", Offline = true } }, null);
        Assert.Equal(ErrorReason.ApprovalDenied, (await Assert.ThrowsAsync<BridgeCallException>(() => ConfirmAsync(review))).Error.Reason);
        Assert.Empty(_channel.Questions);
        Assert.Empty(await Audit.ReadAsync(Token));
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("damaged")]
    public async Task Review_AuditFailure_NoDecisionIsRecorded(string failure)
    {
        KnowledgeReview review = Seed();
        Directory.CreateDirectory(_locations.Audit);
        using FileStream? held = failure == "locked" ? new FileStream(Path.Combine(_locations.Audit, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None) : null;
        if (failure == "damaged")
        { File.WriteAllText(Path.Combine(_locations.Audit, "2026-10-27.jsonl"), "broken\n"); }

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => ConfirmAsync(review));

        Assert.Equal(ErrorReason.AuditUnavailable, error.Error.Reason);
        Assert.DoesNotContain(Directory.GetFiles(_locations.Audit, "*.jsonl").SelectMany(File.ReadLines), line => line.Contains("knowledge_review", StringComparison.Ordinal));
        Assert.False(await Service().VerifyAsync(new string('a', 64), review, Token));
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("source")]
    [InlineData("manifest")]
    public async Task Review_ContentChangedWhileEngineerDecides_RecordsNothing(string changed)
    {
        KnowledgeReview review = Seed();
        var channel = new FakeApprovalChannel("engineer", ApprovalDecision.Granted) { Wait = () => Change(changed) };

        await Assert.ThrowsAsync<BridgeCallException>(() => ConfirmAsync(review, channel));

        Assert.Single(channel.Questions);
        Assert.Empty(await Audit.ReadAsync(Token));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    public async Task Review_FakeSourceFile_IsRejectedBeforeAskingEngineer(string failure)
    {
        KnowledgeReview review = Seed();
        string source = Path.Combine(Folder, ReviewFixtures.Guide.Resource.Path);
        if (failure == "missing")
        { File.Delete(source); }
        else
        { File.AppendAllText(source, "modified"); }

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => ConfirmAsync(review));

        Assert.Equal(ErrorReason.PackInvalid, error.Error.Reason);
        Assert.StartsWith("/source/asset:", error.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_channel.Questions);
        Assert.Empty(await Audit.ReadAsync(Token));
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("source")]
    [InlineData("manifest")]
    public async Task Verify_ChangesAfterConfirmation_RejectsOldDigest(string changed)
    {
        KnowledgeReview review = Seed();
        string digest = await ConfirmAsync(review);
        Change(changed);

        Assert.False(await Service().VerifyAsync(digest, review, Token));
    }

    [Fact]
    public async Task Verify_PromotionTargetChangesOrDecisionDiffers_IsNotTheReviewedContent()
    {
        KnowledgeReview review = Seed(decision: "promote");
        string digest = await ConfirmAsync(review);

        Assert.False(await Service().VerifyAsync(digest, review with { TargetContent = [.. review.TargetContent!, (byte)'\n'] }, Token));
        Assert.False(await Service().VerifyAsync(digest, review with { Decision = "accept", TargetResource = null, TargetContent = null }, Token));
    }

    [Fact]
    public async Task Verify_LaterRejection_SupersedesEarlierAcceptance()
    {
        KnowledgeReview review = Seed();
        string accepted = await ConfirmAsync(review);
        string rejected = await ConfirmAsync(review with { Decision = "reject" });

        Assert.False(await Service().VerifyAsync(accepted, review, Token));
        Assert.True(await Service().VerifyAsync(rejected, review with { Decision = "reject" }, Token));
    }

    [Fact]
    public async Task Verify_TamperedAuditChain_RejectsOtherwiseMatchingRecord()
    {
        KnowledgeReview review = Seed();
        await Audit.AppendAsync(new JsonObject { ["kind"] = "pack_change", ["phase"] = "committed" }, Token);
        string digest = await ConfirmAsync(review);
        string file = Assert.Single(Directory.GetFiles(_locations.Audit, "*.jsonl"));
        File.WriteAllText(file, File.ReadAllText(file).Replace("pack_change", "fake_change", StringComparison.Ordinal));

        Assert.False(await Service().VerifyAsync(digest, review, Token));
    }

    [Fact]
    public async Task Verify_ImportedHistoryAndStanding_RequireFreshLocalConfirmation()
    {
        KnowledgeReview review = Seed();
        var foreignDocument = ReviewFixtures.Document();
        foreignDocument.Spec["status"] = "accepted";
        foreignDocument.Spec["source"] = JsonNode.Parse(review.Source.GetRawText());
        string foreignFolder = Path.Combine(_locations.Root, "foreign");
        KnowledgeReview imported = Seed(document: foreignDocument, folder: foreignFolder, version: "1.0.0", standing: KnowledgeStanding.Official);
        JsonObject history = KnowledgeReviewService.Bind(imported, ReviewFixtures.Entry(foreignDocument, "1.0.0"), ReviewFixtures.Guide with { Version = "1.0.0" });
        history["kind"] = "knowledge_review";
        history["confirmation"] = "Granted";
        history["channel"] = "foreign-engineer";
        string foreignAudit = Path.Combine(_locations.Root, "foreign-audit");
        string foreignDigest = await new AuditLog(foreignAudit, _clock).AppendAsync(history, Token);
        byte[] historicalBytes = File.ReadAllBytes(Assert.Single(Directory.GetFiles(foreignAudit, "*.jsonl")));
        string historyPath = "resources/docs/review.txt";
        File.WriteAllBytes(Path.Combine(foreignFolder, historyPath), historicalBytes);
        PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(foreignFolder, PackManifest.FileName)));
        byte[] manifestBytes = PackJson.Serialize(manifest with
        {
            Resources = [.. manifest.Resources, new PackResource
            {
                Path = historyPath, Kind = ResourceKind.Document, Storage = ResourceStorage.Bundled, Files = 1,
                Bytes = historicalBytes.Length, Sha256 = Host.Plans.PlanStore.Hash(historicalBytes),
            }],
        });
        File.WriteAllBytes(Path.Combine(foreignFolder, PackManifest.FileName), manifestBytes);
        File.WriteAllBytes(Path.Combine(foreignFolder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(new() { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(manifestBytes) }, [])));
        string archive = Path.Combine(_locations.Root, "foreign.eplanpack");
        await PackArchive.ExportAsync(foreignFolder, archive, Token);
        Assert.Equal(PackInstallOutcome.Installed, (await Library.Installed.InstallAsync(archive, Token)).Outcome);
        string install = await Audit.AppendAsync(new JsonObject { ["kind"] = "pack_change", ["phase"] = "committed", ["reviewDigest"] = foreignDigest, ["confirmation"] = "Granted", ["channel"] = "engineer" }, Token);

        Assert.False(await Service().VerifyAsync(foreignDigest, imported, Token));
        Assert.False(await Service().VerifyAsync(install, imported, Token));
        string local = await ConfirmAsync(imported);
        Assert.True(await Service().VerifyAsync(local, imported, Token));
        Assert.Equal(PackJson.Serialize(foreignDocument), File.ReadAllBytes(Path.Combine(Library.Installed.FolderOf("acme.panels", "1.0.0"), KnowledgeDocument.PathOf(KnowledgeKind.Candidate, "motor"))));
    }

    private KnowledgeReviewService Service(IApprovalChannel? channel = null) => new(Library, Audit,
        new ApprovalService([channel ?? _channel], HostConfig.Defaults(_locations), _clock, NullLogger<ApprovalService>.Instance), _bridge);

    private Task<string> ConfirmAsync(KnowledgeReview review, IApprovalChannel? channel = null) => Service(channel).ReviewAsync(review, "review", null, Token);

    private KnowledgeReview Seed(KnowledgeKind kind = KnowledgeKind.Candidate, string decision = "accept", KnowledgeDocument? document = null,
        string? folder = null, string version = "dev", KnowledgeStanding standing = KnowledgeStanding.Candidate)
    {
        folder ??= Folder;
        AssetEntry entry = ReviewFixtures.Entry(document ?? ReviewFixtures.Document(kind), version);
        foreach ((string relative, byte[] bytes) in new[] { (entry.Resource.Path, PackJson.Serialize(entry.Document!)), (ReviewFixtures.Guide.Resource.Path, ReviewFixtures.GuideBytes) })
        {
            string file = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }
        var manifest = new PackManifest
        {
            SchemaVersion = 2,
            Id = "acme.panels",
            Name = "ACME",
            Version = version == "dev" ? DevPackStore.ManifestVersion : version,
            Compatibility = new(),
            Resources = [entry.Resource, ReviewFixtures.Guide.Resource],
            Rules = kind == KnowledgeKind.Candidate ? [new() { Path = entry.Resource.Path, Standing = standing }] : [],
            Provenance = new() { CreatedAt = _clock.GetUtcNow(), CreatedBy = "eplanmcp test", Inputs = [] },
        };
        byte[] manifestBytes = PackJson.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), manifestBytes);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(new() { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(manifestBytes) }, [])));
        return ReviewFixtures.Review(entry, decision);
    }

    private void Change(string changed)
    {
        if (changed != "manifest")
        {
            string path = changed == "source" ? ReviewFixtures.Guide.Resource.Path : KnowledgeDocument.PathOf(KnowledgeKind.Candidate, "motor");
            File.AppendAllText(Path.Combine(Folder, path), "\n");
            return;
        }
        string manifestFile = Path.Combine(Folder, PackManifest.FileName);
        PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(manifestFile));
        string resourceFile = Path.Combine(Folder, manifest.Resources[0].Path);
        byte[] bytes = [.. File.ReadAllBytes(resourceFile), (byte)'\n'];
        File.WriteAllBytes(resourceFile, bytes);
        File.WriteAllBytes(manifestFile, PackJson.Serialize(manifest with { Resources = [manifest.Resources[0] with { Sha256 = Host.Plans.PlanStore.Hash(bytes), Bytes = bytes.Length }, manifest.Resources[1]] }));
    }

    private Dictionary<string, byte[]> FilesOfPack() => Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllBytes, StringComparer.Ordinal);

    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(_locations.Root, "*", SearchOption.AllDirectories))
        { File.SetAttributes(file, FileAttributes.Normal); }
        Directory.Delete(_locations.Root, recursive: true);
    }
}
