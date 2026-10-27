using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ContextBuilderReadTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-context-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 23, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new() { Status = new([], new() { Operations = [OperationNames.ProjectSnapshot] }, null) };
    private PackLibrary Packs => new(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, _time), _time);
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Key => PlanSnapshotHashes.ProjectKey(ContextTestSnapshot.ProjectPath);

    public ContextBuilderReadTests() => _bridge.Handlers[OperationNames.ProjectSnapshot] = a => ContextTestSnapshot.Data((ProjectSnapshotArgs)a!);

    [Fact]
    public async Task NoConfig_DefaultProject_WholeStructures_FreshEveryCall_AndNothingWritten()
    {
        JsonElement first = Data(await Get());
        _bridge.Handlers[OperationNames.ProjectSnapshot] = a => ContextTestSnapshot.Data((ProjectSnapshotArgs)a!, changed: true);
        JsonElement second = Data(await Get());
        Assert.Equal("Line1", first.GetProperty("project").GetProperty("name").GetString());
        Assert.Equal(Key, first.GetProperty("project").GetProperty("path").GetString());
        Assert.Equal(2, first.GetProperty("coverage").GetProperty("pages").GetArrayLength());
        Assert.Equal(4, first.GetProperty("objects").GetArrayLength());
        Assert.Equal(4, first.GetProperty("facts").GetArrayLength());
        Assert.Equal(4, first.GetProperty("sources").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("contextRef").GetProperty("bindingRevision").ValueKind);
        Assert.NotEqual(first.GetProperty("contextRef").GetProperty("snapshotHash").GetString(), second.GetProperty("contextRef").GetProperty("snapshotHash").GetString());
        Assert.Empty(first.GetProperty("config").EnumerateArray());
        Assert.Contains(first.GetProperty("coverage").GetProperty("missing").EnumerateArray(), v => v.GetString() == "config:binding");
        Assert.Equal(2, _bridge.Calls.Count);
        Assert.All(_bridge.Calls, c => Assert.Equal(OperationNames.ProjectSnapshot, c.Operation));
        Assert.Empty(_bridge.Writes);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_locations.Root));
        AssertSources(first);
    }

    [Fact]
    public async Task DefaultBinding_ReturnsRealRevisionCanonicalDigest_AndExactResourceSources()
    {
        await Install("1.0.0");
        ProjectBinding binding = await Bind("default", "1.0.0");
        byte[] before = File.ReadAllBytes(BindingFile);
        await Install("2.0.0");
        JsonElement result = Data(await Get(include: "[\"rules\",\"modules\",\"objects\",\"facts\",\"config\"]"));
        Assert.Equal(42, result.GetProperty("contextRef").GetProperty("bindingRevision").GetInt32());
        Assert.Equal(ContextBuilder.Reference(null, binding)["lockDigest"]!.GetValue<string>(), result.GetProperty("contextRef").GetProperty("lockDigest").GetString());
        Assert.Equal(before, File.ReadAllBytes(BindingFile));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(BindingFile)!, ".binding.lock")));
        Assert.All(result.GetProperty("sources").EnumerateArray().Where(s => s.TryGetProperty("pack", out _)), s =>
        {
            Assert.Equal("1.0.0", s.GetProperty("exactVersion").GetString());
            Assert.Equal(binding.Locks[0].Digest, s.GetProperty("manifestDigest").GetString());
            Assert.Equal(64, s.GetProperty("fileHash").GetString()!.Length);
        });
        Assert.Equal("candidate", Assert.Single(result.GetProperty("rules").EnumerateArray()).GetProperty("category").GetString());
        Assert.Equal(2, result.GetProperty("modules").GetArrayLength());
        Assert.DoesNotContain("SourceProject", result.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateUser", result.GetRawText(), StringComparison.Ordinal);
        AssertSources(result);
    }

    [Theory]
    [InlineData("default", "second")]
    [InlineData("panel")]
    public async Task MultiOrNonDefaultBinding_IsRefused(params string[] cabinets)
    {
        await WriteBinding(new()
        {
            BindingVersion = 1,
            Project = Key,
            Revision = 2,
            Cabinets = [.. cabinets.Select(id => new CabinetBinding { Id = id })]
        });
        Assert.Equal(ErrorReason.CapabilityMissing, (await ErrorAsync(() => Get())).Reason);
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task BindingWithoutCabinets_DoesNotInventDefaultBinding()
    {
        await WriteBinding(new()
        {
            BindingVersion = 1,
            Project = Key,
            Revision = 9
        });
        JsonElement reference = Data(await Get()).GetProperty("contextRef");
        Assert.Equal(JsonValueKind.Null, reference.GetProperty("bindingRevision").ValueKind);
        Assert.Equal(JsonValueKind.Null, reference.GetProperty("lockDigest").ValueKind);
    }

    [Fact]
    public async Task Scope_IntersectsStructurePageKindAndLocator_AndHasOnlyLocalHash()
    {
        JsonElement result = Data(await Get("{\"pages\":[\"+A1/1\",\"+A10/2\"],\"structures\":[\"+A1\"],\"kinds\":[\"function\"],\"locators\":[{\"project\":\"D:\\\\Fake\\\\Line1\",\"kind\":\"function\",\"type\":17,\"oid\":29,\"page\":\"+A1/1\"}]}"));
        Assert.Single(result.GetProperty("objects").EnumerateArray());
        Assert.Equal("+A1/1", Assert.Single(result.GetProperty("coverage").GetProperty("pages").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("contextRef").GetProperty("snapshotHash").ValueKind);
        AssertSources(result);
    }

    [Fact]
    public async Task Scope_ExistingLocatorOutsidePageFilter_SelectsEmptyIntersection()
    {
        string scope = JsonSerializer.Serialize(new
        {
            pages = new List<string> { "+A10/2" },
            locators = new List<ObjectLocator> { new() { Project = ContextTestSnapshot.ProjectPath, Kind = "function", Type = 17, Oid = 29, Page = "+A1/1" } }
        }, HostJson.Options);
        JsonElement result = Data(await Get(scope));
        Assert.Empty(result.GetProperty("objects").EnumerateArray());
        Assert.Empty(result.GetProperty("facts").EnumerateArray());
        Assert.Empty(result.GetProperty("sources").EnumerateArray());
        Assert.Empty(result.GetProperty("coverage").GetProperty("pages").EnumerateArray());
    }

    [Fact]
    public async Task StableSources_IgnoreExtractionTime_AndIncludeInputOrder()
    {
        JsonElement first = Data(await Get(include: "[\"objects\",\"facts\"]"));
        _time.Advance(TimeSpan.FromMinutes(1));
        JsonElement second = Data(await Get(include: "[\"facts\",\"objects\"]"));
        Assert.Equal(first.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("id").GetString()), second.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("id").GetString()));
        Assert.True(JsonElement.DeepEquals(first.GetProperty("objects"), second.GetProperty("objects")));
        Assert.True(JsonElement.DeepEquals(first.GetProperty("facts"), second.GetProperty("facts")));
        Assert.NotEqual(first.GetProperty("sources")[0].GetProperty("extractedAt").GetString(), second.GetProperty("sources")[0].GetProperty("extractedAt").GetString());
    }

    [Fact]
    public async Task CancellationBeforeExtraction_DoesNotReadOrWrite()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContextBuilder.ReadAsync(_bridge, Packs, null, Parse("{}"), ContextBuilder.Defaults, 5000, null, new CancellationToken(canceled: true)));
        Assert.Empty(_bridge.Calls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_locations.Root));
    }

    [Fact]
    public async Task ChunkedSnapshot_ComposesWholeHash_StableItemsAndPageHashes()
    {
        _bridge.Handlers[OperationNames.ProjectSnapshot] = a => ContextTestSnapshot.Data((ProjectSnapshotArgs)a!, split: true);
        JsonElement result = Data(await Get());
        Assert.Equal(2, _bridge.Calls.Count);
        Assert.Equal(PlanSnapshotHashes.Compose(ContextTestSnapshot.Data(new()).Pages), result.GetProperty("contextRef").GetProperty("snapshotHash").GetString());
        Assert.Equal(["+A1/1", "+A10/2"], result.GetProperty("coverage").GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(["+A1-K1", "+A1/1", "+A10-K1", "+A10/2"], result.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("locator").GetProperty("name").GetString()));
    }

    [Theory]
    [InlineData("{\"pages\":[\"absent\"]}")]
    [InlineData("{\"locators\":[{\"project\":\"D:\\\\Copy\\\\Line1\",\"kind\":\"function\",\"type\":17,\"oid\":29}]}")]
    public async Task ExplicitMissingPageOrOtherProjectLocator_IsResourceNotFound(string scope)
    {
        Assert.Equal(ErrorReason.ResourceNotFound, (await ErrorAsync(() => Get(scope))).Reason);
    }

    [Fact]
    public async Task MissingSnapshotCapability_OrInconsistentHash_IsCapabilityMissing()
    {
        _bridge.Status = new([], new()
        {
            Operations = []
        }, null);
        Assert.Equal(ErrorReason.CapabilityMissing, (await ErrorAsync(() => Get())).Reason);
        Assert.Empty(_bridge.Calls);
        _bridge.Status = new([], new()
        {
            Operations = [OperationNames.ProjectSnapshot]
        }, null);
        _bridge.Handlers[OperationNames.ProjectSnapshot] = a => { ProjectSnapshotData d = ContextTestSnapshot.Data((ProjectSnapshotArgs)a!); d.Pages[0].Hash = new string('0', 64); return d; };
        Assert.Equal(ErrorReason.CapabilityMissing, (await ErrorAsync(() => Get())).Reason);
    }

    [Fact]
    public async Task MissingLockedVersion_NeverFallsBackToLatest()
    {
        await Install("2.0.0");
        await WriteBinding(new()
        {
            BindingVersion = 1,
            Project = Key,
            Revision = 3,
            Cabinets = [new() { Id = "default" }],
            Locks = [new() { Id = "acme.panels", Version = "1.0.0", Digest = new string('a', 64) }]
        });
        Assert.Equal(ErrorReason.CapabilityMissing, (await ErrorAsync(() => Get())).Reason);
    }

    private Task<JsonObject> Get(string scope = "{}", string? include = null, int budget = 20000) =>
        ContextBuilder.ReadAsync(_bridge, Packs, null, Parse(scope), include is null ? ContextBuilder.Defaults : JsonSerializer.Deserialize<string[]>(include)!, budget, null, Token);
    private string BindingFile => Path.Combine(Packs.Workspaces.FolderOf(Key), ProjectBinding.FileName);
    private async Task WriteBinding(ProjectBinding binding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BindingFile)!);
        await File.WriteAllBytesAsync(BindingFile, PackJson.Serialize(binding), Token);
    }
    private async Task<ProjectBinding> Bind(string cabinet, string version)
    {
        var binding = new ProjectBinding { BindingVersion = 1, Project = Key, Revision = 42, Cabinets = [new() { Id = cabinet }], Locks = [new() { Id = "acme.panels", Version = version, Digest = Packs.Installed.InstalledDigest("acme.panels", version)! }] };
        await WriteBinding(binding);
        return binding;
    }
    private async Task Install(string version)
    {
        string input = Path.Combine(_locations.Root, "input");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "Motor.ema"), "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" SourceProject=\"C:\\Users\\PrivateUser\\secret\" />", Token);
        ScanResult scan = await ResourceScanner.ScanAsync([new ScanInput(input)], Token);
        await Packs.Dev.ImportAsync("acme.panels", "ACME panels", scan, false, "test", _time.GetUtcNow(), Token);
        foreach ((KnowledgeKind kind, string name) in new[] { (KnowledgeKind.Candidate, "voltage"), (KnowledgeKind.Module, "motor") })
        {
            await Packs.Dev.PutAsync("acme.panels", new()
            {
                ApiVersion = KnowledgeDocument.CurrentApiVersion,
                Kind = kind,
                Metadata = new()
                {
                    Name = name,
                    Title = name
                },
                Origin = new()
                {
                    Asset = "macro/input/Motor.ema",
                    Page = "+A1/1"
                },
                Spec = new JsonObject { ["description"] = "existing material" }
            }, "test", _time.GetUtcNow(), Token);
        }
        var built = await Packs.Dev.BuildAsync("acme.panels", version, Packs.Installed, Packs.Config.PackSources, "test", _time.GetUtcNow(), Token);
        Assert.NotEqual(PackInstallOutcome.Refused, built.Result.Outcome);
        Assert.Empty(built.Result.Problems);
    }
    internal static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static JsonElement Data(JsonObject result) => JsonSerializer.SerializeToElement(result, HostJson.Options);
    private static async Task<BridgeError> ErrorAsync(Func<Task<JsonObject>> read)
    {
        BridgeCallException exception = await Assert.ThrowsAsync<BridgeCallException>(read);
        return exception.Error;
    }
    internal static void AssertSources(JsonElement result)
    {
        Dictionary<string, JsonElement> sources = result.GetProperty("sources").EnumerateArray().ToDictionary(s => s.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (string section in ContextBuilder.Sections.Where(s => result.TryGetProperty(s, out _)))
        {
            foreach (JsonElement item in result.GetProperty(section).EnumerateArray())
            {
                foreach (JsonElement id in item.GetProperty("sources").EnumerateArray())
                {
                    Assert.True(sources.ContainsKey(id.GetString()!));
                    used.Add(id.GetString()!);
                }
            }
        }
        Assert.Equal(sources.Keys.Order(StringComparer.Ordinal), used.Order(StringComparer.Ordinal));
    }
    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_locations.Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_locations.Root, recursive: true);
    }
}
