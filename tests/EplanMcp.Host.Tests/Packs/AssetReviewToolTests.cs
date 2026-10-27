using System.Globalization;
using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class AssetReviewToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-asset-review-").FullName);
    private readonly FakeBridgeClient _bridge = new() { Status = new([], new() { Eplan = new() { ProcessId = 42, Version = "2026.0.3" } }, null) };
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private AuditLog Audit => new(_locations.Audit, TimeProvider.System) { LockWait = TimeSpan.Zero };
    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), Audit, TimeProvider.System);
    private string Folder => Packs.Dev.FolderOf("acme.panels");
    private string Installed => Packs.Installed.FolderOf("acme.panels", "1.0.0");

    [Theory]
    [InlineData("accept", true)]
    [InlineData("reject", true)]
    [InlineData("close_gap", true)]
    [InlineData("promote", true)]
    [InlineData("accept", false)]
    [InlineData("reject", false)]
    [InlineData("close_gap", false)]
    [InlineData("promote", false)]
    public async Task McpReview_EngineerGrantedOrDenied_RecordsDecisionAndPublishesOnlyConfirmedPromotion(string decision, bool granted)
    {
        KnowledgeReview review = Seed(decision);
        Dictionary<string, byte[]> before = Files();
        var channel = new FakeApprovalChannel("engineer", granted ? ApprovalDecision.Granted : ApprovalDecision.Denied);
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_locations);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(_bridge);
        builder.Services.AddSingleton(ServiceApprovals(channel));
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            var arguments = Arguments(review);
            CallToolResult result = await client.CallToolAsync(AssetTools.ReviewTool, arguments, cancellationToken: Token);
            Assert.True(channel.Questions.Count > 0, ((TextContentBlock)result.Content[0]).Text);
            ApprovalQuestion question = Assert.Single(channel.Questions);
            Assert.Equal(AssetTools.ReviewTool, question.Tool);
            foreach (string language in new[] { "zh-CN", "en-US" })
            {
                ApprovalAskArgs texts = ApprovalTexts.For(question, CultureInfo.GetCultureInfo(language));
                Assert.Contains(language == "zh-CN" ? "知识审核" : "knowledge review", texts.Title, StringComparison.Ordinal);
                Assert.Contains(language == "zh-CN" ? "接受候选不授予正式资格" : "Acceptance grants no official standing", texts.Note, StringComparison.Ordinal);
            }
            if (!granted)
            {
                Assert.Equal(ErrorReason.ApprovalDenied, Error(result));
                Assert.DoesNotContain(await Audit.ReadAsync(Token), r => (string?)r["kind"] == "knowledge_review");
                AssertFiles(before);
                Assert.Equal(before.Count, Files().Count);
                return;
            }
            JsonElement data = Data(result);
            Assert.Equal(decision, data.GetProperty("decision").GetString());
            Assert.True(await Service(channel).VerifyAsync(data.GetProperty("reviewDigest").GetString()!, review, Token));
            Assert.False(data.TryGetProperty("execution", out _));
            Assert.Equal(decision == "promote" ? review.TargetResource : null, data.GetProperty("rule").GetString());
            AssertFiles(decision == "promote" ? before.Where(p => !p.Key.EndsWith(PackManifest.FileName, StringComparison.Ordinal) && !p.Key.EndsWith(PackManifest.LockFileName, StringComparison.Ordinal)) : before);
            JsonElement repeated = Data(await client.CallToolAsync(AssetTools.ReviewTool, arguments, cancellationToken: Token));
            Assert.Equal(data.GetProperty("reviewDigest").GetString(), repeated.GetProperty("reviewDigest").GetString());
            Assert.Single(channel.Questions);
            if (decision == "promote")
            {
                Assert.Equal(KnowledgeStanding.Official, Manifest().Rules.Single(r => r.Path.StartsWith("knowledge/rule/", StringComparison.Ordinal)).Standing);
                Data(await client.CallToolAsync(PackTools.BuildTool, new Dictionary<string, object?> { ["pack"] = "acme.panels", ["version"] = "1.0.0" }, cancellationToken: Token));
                Data(await client.CallToolAsync(PackTools.BuildTool, new Dictionary<string, object?> { ["pack"] = "acme.panels", ["version"] = "1.0.0" }, cancellationToken: Token));
                Assert.True(Directory.Exists(Installed));
            }
            Assert.Empty(_bridge.Writes);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Build_HandEditedStandingWithoutLocalReview_IsPackInvalidEvenWhenPackChecksClean(bool candidate)
    {
        KnowledgeReview review = Seed("promote");
        if (!candidate)
        { WriteDocument(KnowledgeDocument.Read(review.TargetContent!)); }
        else
        {
            KnowledgeDocument document = ReviewFixtures.Document();
            document.Spec["source"] = System.Text.Json.Nodes.JsonNode.Parse(review.Source.GetRawText());
            WriteDocument(document);
        }
        PackManifest manifest = Manifest();
        Save(manifest with { Rules = [.. manifest.Rules.Select(s => candidate || s.Path.StartsWith("knowledge/rule/", StringComparison.Ordinal) ? s with { Standing = KnowledgeStanding.Official } : s)] });
        Assert.True((await PackVerifier.CheckDirectoryAsync(Folder, Token)).Ok);

        Assert.Equal(ErrorReason.PackInvalid, Error(await Build()));
        Assert.False(Directory.Exists(Installed));
    }

    [Fact]
    public async Task Build_MandatoryRuleHasNoSource_IsPackInvalidAndInstallsNothing()
    {
        KnowledgeReview review = Seed("promote");
        KnowledgeDocument rule = KnowledgeDocument.Read(review.TargetContent!);
        rule.Spec.Remove("source");
        WriteDocument(rule);

        CallToolResult result = await Build();

        Assert.Equal(ErrorReason.PackInvalid, Error(result));
        Assert.Contains("/spec/source", ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Installed));
    }

    [Fact]
    public async Task Build_OfficialSlotPointsAtOtherKindAndOtherSlotShadowsStanding_IsPackInvalid()
    {
        Seed("close_gap");
        KnowledgeDocument gap = ReviewFixtures.Document(KnowledgeKind.Gap);
        gap.Spec["source"] = "engineer confirmed";
        WriteDocument(gap);
        PackManifest manifest = Manifest();
        string path = KnowledgeDocument.PathOf(KnowledgeKind.Gap, "motor");
        Save(manifest with
        {
            Rules = [.. manifest.Rules, new() { Path = path, Standing = KnowledgeStanding.Official }],
            Docs = [new() { Path = path, Standing = KnowledgeStanding.Candidate }],
        });
        Assert.True((await PackVerifier.CheckDirectoryAsync(Folder, Token)).Ok);
        Assert.Equal(ErrorReason.PackInvalid, Error(await Build()));
    }

    [Fact]
    public async Task Promotion_ConfigOperand_UsesVerifiedPackSchema()
    {
        KnowledgeReview review = Seed("promote");
        WriteDocument(new KnowledgeDocument
        {
            ApiVersion = KnowledgeDocument.V2ApiVersion,
            Kind = KnowledgeKind.ConfigSchema,
            Metadata = new() { Name = "motors" },
            Spec = System.Text.Json.Nodes.JsonNode.Parse("""{"items":{"motor.starter":{"type":"choice","enum":["dol","star_delta"],"allowedLayers":["base"],"required":true}}}""")!.AsObject(),
        });
        KnowledgeDocument rule = KnowledgeDocument.Read(review.TargetContent!);
        rule.Spec["check"] = System.Text.Json.Nodes.JsonNode.Parse("""{"eq":[{"config":"motor.starter"},{"literal":"dol"}]}""");
        review = review with { TargetContent = PackJson.Serialize(rule) };

        Data(await Review(review, new FakeApprovalChannel("engineer", ApprovalDecision.Granted)));
        Data(await Build());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"source\":null}")]
    [InlineData("{\"source\":{}}")]
    [InlineData("{\"source\":\"\"}")]
    [InlineData("{\"source\":false}")]
    public async Task Build_LegacyMandatoryWithoutSource_AlsoFails(string spec)
    {
        Seed("accept");
        var body = System.Text.Json.Nodes.JsonNode.Parse(spec)!.AsObject();
        body["severity"] = "mandatory";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { spec = body });
        string path = "resources/rules/mandatory.json";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Folder, path))!);
        File.WriteAllBytes(Path.Combine(Folder, path), bytes);
        PackManifest manifest = Manifest();
        Save(manifest with
        {
            Resources = [.. manifest.Resources, new() { Path = path, Kind = ResourceKind.Rule, Storage = ResourceStorage.Bundled, Bytes = bytes.Length, Files = 1, Sha256 = PlanStore.Hash(bytes) }],
            Rules = [.. manifest.Rules, new() { Path = path, Standing = KnowledgeStanding.Candidate }],
        });

        Assert.Equal(ErrorReason.PackInvalid, Error(await Build()));
        Assert.False(Directory.Exists(Installed));
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("source")]
    [InlineData("rule")]
    [InlineData("rejected")]
    [InlineData("audit")]
    public async Task Build_ReviewedEvidenceChanged_RejectsOldConfirmation(string changed)
    {
        KnowledgeReview review = Seed("promote");
        var channel = new FakeApprovalChannel("engineer", ApprovalDecision.Granted);
        Data(await Review(review, channel));
        if (changed == "rejected")
        { Data(await Review(review with { Decision = "reject", TargetResource = null, TargetContent = null }, channel)); }
        else if (changed == "audit")
        {
            string file = Assert.Single(Directory.GetFiles(_locations.Audit, "*.jsonl"));
            File.WriteAllText(file, File.ReadAllText(file).Replace("Granted", "Forged!", StringComparison.Ordinal));
        }
        else
        {
            string path = changed == "candidate" ? KnowledgeDocument.PathOf(KnowledgeKind.Candidate, "motor") : changed == "rule" ? KnowledgeDocument.PathOf(KnowledgeKind.Rule, "motor") : ReviewFixtures.Guide.Resource.Path;
            string file = Path.Combine(Folder, path);
            File.AppendAllText(file, "\n");
            byte[] bytes = File.ReadAllBytes(file);
            PackManifest manifest = Manifest();
            Save(manifest with { Resources = [.. manifest.Resources.Select(r => r.Path == path ? r with { Sha256 = PlanStore.Hash(bytes), Bytes = bytes.Length } : r)] });
            Assert.True((await PackVerifier.CheckDirectoryAsync(Folder, Token)).Ok);
        }

        Assert.Equal(ErrorReason.PackInvalid, Error(await Build()));
        Assert.False(Directory.Exists(Installed));
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("confirmation")]
    public async Task Review_ChangedBindingsOrExistingTarget_NeverPublishes(string changed)
    {
        KnowledgeReview review = Seed("promote");
        if (changed == "digest")
        { review = review with { ContentDigest = new string('a', 64) }; }
        if (changed == "target")
        {
            KnowledgeDocument target = KnowledgeDocument.Read(review.TargetContent!);
            target.Spec["message"]!["en"] = "Different content";
            WriteDocument(target);
        }
        var channel = new FakeApprovalChannel("engineer", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                if (changed == "source")
                { File.AppendAllText(Path.Combine(Folder, ReviewFixtures.Guide.Resource.Path), "changed"); }
            },
            Bind = answer => changed == "confirmation" ? answer with { ArgumentsHash = "unbound" } : answer,
        };
        CallToolResult result = await Review(review, channel);

        Assert.True(result.IsError);
        Assert.Equal(changed == "target", File.Exists(Path.Combine(Folder, KnowledgeDocument.PathOf(KnowledgeKind.Rule, "motor"))));
        Assert.False(Directory.Exists(Installed));
    }

    private KnowledgeReview Seed(string decision)
    {
        KnowledgeDocument document = ReviewFixtures.Document(decision == "close_gap" ? KnowledgeKind.Gap : KnowledgeKind.Candidate);
        AssetEntry entry = ReviewFixtures.Entry(document);
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(Path.Combine(Folder, "resources", "docs"));
        File.WriteAllBytes(Path.Combine(Folder, ReviewFixtures.Guide.Resource.Path), ReviewFixtures.GuideBytes);
        Save(new PackManifest
        {
            SchemaVersion = 2,
            Id = "acme.panels",
            Name = "ACME",
            Version = DevPackStore.ManifestVersion,
            Compatibility = new(),
            Resources = [ReviewFixtures.Guide.Resource],
            Provenance = new() { CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "eplanmcp test", Inputs = [] },
        });
        WriteDocument(document);
        return ReviewFixtures.Review(entry, decision);
    }

    private void WriteDocument(KnowledgeDocument document)
    {
        AssetEntry entry = ReviewFixtures.Entry(document);
        string path = Path.Combine(Folder, entry.Resource.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PackJson.Serialize(document));
        PackManifest manifest = Manifest();
        Save(manifest with
        {
            Resources = [.. manifest.Resources.Where(r => r.Path != entry.Resource.Path), entry.Resource],
            Rules = document.Kind is KnowledgeKind.Candidate or KnowledgeKind.Rule ? [.. manifest.Rules.Where(s => s.Path != entry.Resource.Path), new() { Path = entry.Resource.Path, Standing = KnowledgeStanding.Candidate }] : manifest.Rules,
        });
    }

    private static Dictionary<string, object?> Arguments(KnowledgeReview review) => new()
    {
        ["pack"] = "acme.panels",
        ["asset"] = review.Resource.Split(':')[1],
        ["expected_digest"] = review.ContentDigest,
        ["decision"] = review.Decision,
        ["source"] = review.Source,
        ["rule"] = review.TargetContent is null ? null : JsonSerializer.Deserialize<JsonElement>(review.TargetContent),
    };
    private Task<CallToolResult> Review(KnowledgeReview review, IApprovalChannel channel) => AssetTools.AssetReviewAsync(Packs, Service(channel), "acme.panels", review.Resource.Split(':')[1], review.ContentDigest, review.Decision, review.Source,
        review.TargetContent is null ? null : JsonSerializer.Deserialize<JsonElement>(review.TargetContent), cancellationToken: Token);
    private Task<CallToolResult> Build() => PackTools.PackBuildAsync(Packs, Service(new FakeApprovalChannel("unused", null)), "acme.panels", "1.0.0", Token);
    private ApprovalService ServiceApprovals(IApprovalChannel channel) => new([channel], Packs.Config, Packs.Clock, NullLogger<ApprovalService>.Instance);
    private KnowledgeReviewService Service(IApprovalChannel channel) => new(Packs, Audit, ServiceApprovals(channel), _bridge);
    private PackManifest Manifest() => PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(Folder, PackManifest.FileName)));
    private void Save(PackManifest manifest)
    {
        manifest = manifest with { Resources = [.. manifest.Resources.OrderBy(r => r.Path, StringComparer.Ordinal)], Rules = [.. manifest.Rules.OrderBy(s => s.Path, StringComparer.Ordinal)] };
        byte[] bytes = PackJson.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(Folder, PackManifest.FileName), bytes);
        File.WriteAllBytes(Path.Combine(Folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(new() { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(bytes) }, [])));
    }
    private Dictionary<string, byte[]> Files() => Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes, StringComparer.Ordinal);
    private static void AssertFiles(IEnumerable<KeyValuePair<string, byte[]>> before) => Assert.All(before, p => Assert.Equal(p.Value, File.ReadAllBytes(p.Key)));
    private static JsonElement Data(CallToolResult result) => WriteToolHarness.Structured(result);
    private static string? Error(CallToolResult result) => WriteToolHarness.Error(result).GetProperty("reason").GetString();
    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(_locations.Root, "*", SearchOption.AllDirectories))
        { File.SetAttributes(file, FileAttributes.Normal); }
        Directory.Delete(_locations.Root, recursive: true);
    }
}
