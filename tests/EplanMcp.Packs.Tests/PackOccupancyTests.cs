namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PackOccupancyTests
{
    private readonly Dictionary<string, (PackManifest Manifest, PackLock Lock)> _packs = new(StringComparer.Ordinal);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Resolve_BindingPinsOnlyRoot_ProtectsExactTransitiveClosure(int version)
    {
        PackReference leaf = Add("leaf");
        PackReference middle = Add("middle", closure: [leaf]);
        PackReference root = Add("root", closure: [leaf, middle], dependencies: [new() { Id = middle.Id, Version = "^1.0.0" }]);
        Add("leaf", "2.0.0");
        ProjectBinding binding = Binding(version) with { Locks = [root] };

        IReadOnlySet<(string Id, string Version)> occupied = await PackOccupancy.ResolveAsync([binding], ReadAsync, Token);

        Assert.Equal(3, occupied.Count);
        Assert.Contains(("root", "1.0.0"), occupied);
        Assert.Contains(("middle", "1.0.0"), occupied);
        Assert.Contains(("leaf", "1.0.0"), occupied);
        Assert.DoesNotContain(("leaf", "2.0.0"), occupied);
    }

    [Fact]
    public async Task Resolve_V2RootsLayersAndResidualLocks_AllProtectVersions()
    {
        PackReference explicitLock = Add("residual");
        Add("module");
        Add("company", layer: true);
        ProjectBinding binding = Binding(2) with
        {
            Locks = [explicitLock],
            PackRoots = [new() { Id = "module", Version = "1.0.0" }],
            Cabinets = [new() { Id = "a", Layers = ["company@1.0.0:config_layer/test"] }],
        };

        IReadOnlySet<(string Id, string Version)> occupied = await PackOccupancy.ResolveAsync([binding], ReadAsync, Token);

        Assert.Equal(3, occupied.Count);
        Assert.Contains(("residual", "1.0.0"), occupied);
        Assert.Contains(("module", "1.0.0"), occupied);
        Assert.Contains(("company", "1.0.0"), occupied);
    }

    [Fact]
    public async Task Resolve_DifferentProjectsLockDifferentVersions_ProtectsBoth()
    {
        ProjectBinding first = Binding(1) with { Locks = [Add("shared")] };
        ProjectBinding second = Binding(2) with { Project = "other", Locks = [Add("shared", "2.0.0")] };

        IReadOnlySet<(string Id, string Version)> occupied = await PackOccupancy.ResolveAsync([first, second], ReadAsync, Token);

        Assert.Equal(2, occupied.Count);
        Assert.Contains(("shared", "1.0.0"), occupied);
        Assert.Contains(("shared", "2.0.0"), occupied);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Resolve_BindingDigestDiffers_RejectsWithLockPointer(int version)
    {
        ProjectBinding binding = Binding(version) with { Locks = [Add("root") with { Digest = Manifests.Hash('b') }] };

        PackFormatException error = await Assert.ThrowsAsync<PackFormatException>(() => PackOccupancy.ResolveAsync([binding], ReadAsync, Token));

        Assert.StartsWith("/locks/0:", error.Message, StringComparison.Ordinal);
    }

    private PackReference Add(string id, string version = "1.0.0", PackReference[]? closure = null, PackDependency[]? dependencies = null, bool layer = false)
    {
        PackManifest manifest = Manifests.Minimal() with
        {
            Id = id,
            Version = version,
            Dependencies = dependencies ?? [.. (closure ?? []).Select(p => new PackDependency { Id = p.Id, Version = p.Version })],
            Resources = layer ? [Manifests.File("knowledge/config_layer/test.json", ResourceKind.ConfigLayer)] : [],
        };
        var pin = new PackReference { Id = id, Version = version, Digest = Manifests.Hash('a') };
        _packs[id + "@" + version] = (manifest, PackLock.Create(pin, closure ?? []));
        return pin;
    }

    private Task<(PackManifest, PackLock)> ReadAsync(string id, string version, CancellationToken cancellationToken)
    {
        Assert.Equal(Token, cancellationToken);
        return Task.FromResult(_packs[id + "@" + version]);
    }

    private static ProjectBinding Binding(int version) => new()
    {
        BindingVersion = version,
        Project = "plant",
        Revision = 0,
        ProjectValues = version == 2 ? new() : null,
        PackRoots = version == 2 ? [] : null,
    };
}
