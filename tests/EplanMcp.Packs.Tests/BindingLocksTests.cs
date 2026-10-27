using System.Text;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class BindingLocksTests
{
    private readonly Dictionary<string, (PackManifest Manifest, PackLock Lock)> _packs = new(StringComparer.Ordinal);
    private readonly List<string> _reads = [];
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resolve_ExplicitRootsAndAllCabinetLayers_UsesFrozenClosureOnce()
    {
        PackReference shared = Add("shared", "1.0.0");
        Add("shared", "2.0.0");
        Add("module", "1.0.0", [shared]);
        PackReference company = Add("company", "1.0.0", [shared], layer: true);
        Add("cabinet", "1.0.0", [company, shared], [new() { Id = "company", Version = "^1.0.0" }], layer: true);
        ProjectBinding binding = Binding() with
        {
            PackRoots = [new() { Id = "module", Version = "1.0.0" }],
            Cabinets = [Cabinet("a", "company@1.0.0:config_layer/test"), Cabinet("b", "cabinet@1.0.0:config_layer/test")],
        };

        IReadOnlyList<PackReference> resolved = await BindingLocks.ResolveAsync(binding, ReadAsync, Token);

        Assert.Equal(["cabinet", "company", "module", "shared"], resolved.Select(p => p.Id));
        Assert.All(resolved, p => Assert.Equal("1.0.0", p.Version));
        Assert.Equal(4, _reads.Count);
        Assert.Equal(4, _reads.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("versions")]
    [InlineData("range")]
    [InlineData("digest")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("cycle")]
    [InlineData("transitive")]
    [InlineData("layer")]
    public async Task Resolve_IncompatibleOrForgedClosure_RejectsWithPointer(string conflict)
    {
        PackReference leaf = Add("leaf", "1.0.0");
        PackReference other = Add("other", "1.0.0");
        Add("root", "1.0.0", [leaf]);
        ProjectBinding binding = Binding() with { PackRoots = [new() { Id = "root", Version = "1.0.0" }] };
        switch (conflict)
        {
            case "versions":
                Add("leaf", "2.0.0");
                binding = binding with { PackRoots = [.. binding.PackRoots!, new() { Id = "leaf", Version = "2.0.0" }] };
                break;
            case "range":
                Add("root", "1.0.0", [leaf], [new() { Id = "leaf", Version = "^2.0.0" }]);
                break;
            case "digest":
                Add("root", "1.0.0", [leaf with { Digest = Manifests.Hash('b') }]);
                break;
            case "missing":
                Add("root", "1.0.0", [], [new() { Id = "leaf", Version = "^1.0.0" }]);
                break;
            case "extra":
                Add("root", "1.0.0", [leaf, other], [new() { Id = "leaf", Version = "^1.0.0" }]);
                break;
            case "cycle":
                PackReference root = _packs["root@1.0.0"].Lock.Pack;
                Add("leaf", "1.0.0", [root]);
                break;
            case "transitive":
                Add("leaf", "1.0.0", [other]);
                break;
            case "layer":
                binding = binding with { Cabinets = [Cabinet("a", "leaf@1.0.0:config_layer/absent")] };
                break;
        }

        PackFormatException failure = await Assert.ThrowsAsync<PackFormatException>(() => BindingLocks.ResolveAsync(binding, ReadAsync, Token));
        Assert.StartsWith(conflict == "layer" ? "/cabinets/0/layers/0:" : "/packRoots/", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_EmptyRoots_RemovesUnusedLocks()
    {
        ProjectBinding binding = Binding() with { Locks = [Add("unused", "1.0.0")] };
        Assert.Empty(await BindingLocks.ResolveAsync(binding, ReadAsync, Token));
        Assert.Empty(_reads);
    }

    private PackReference Add(string id, string version, PackReference[]? closure = null, PackDependency[]? dependencies = null, bool layer = false)
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
        _reads.Add(id + "@" + version);
        return Task.FromResult(_packs[id + "@" + version]);
    }

    private static ProjectBinding Binding() => PackJson.ReadBinding(Encoding.UTF8.GetBytes("""
        {"bindingVersion":2,"project":"plant","revision":0,"projectValues":{},"packRoots":[],"locks":[],"cabinets":[]}
        """));

    private static CabinetBinding Cabinet(string id, string layer) => new() { Id = id, Layers = [layer] };
}
