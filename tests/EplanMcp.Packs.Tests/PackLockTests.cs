using System.Text;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PackLockTests
{
    private static readonly PackReference _self = new() { Id = "acme.cabinet-base", Version = "1.0.0", Digest = Manifests.Hash('a') };
    private static readonly PackReference _base = new() { Id = "acme.base", Version = "1.4.2", Digest = Manifests.Hash('b') };
    private static readonly PackReference _parts = new() { Id = "acme.parts", Version = "2.1.0", Digest = Manifests.Hash('c') };

    [Fact]
    public void Create_SameResolutionInAnyOrder_GivesTheSameBytes()
    {
        byte[] first = PackJson.Serialize(PackLock.Create(_self, [_parts, _base]));
        byte[] second = PackJson.Serialize(PackLock.Create(_self, [_base, _parts]));

        Assert.Equal(first, second);
        Assert.True(Encoding.UTF8.GetString(first).IndexOf("acme.base", StringComparison.Ordinal) < Encoding.UTF8.GetString(first).IndexOf("acme.parts", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_RoundTrip_GivesTheSameBytes()
    {
        byte[] json = PackJson.Serialize(PackLock.Create(_self, [_base, _parts]));

        Assert.Equal(json, PackJson.Serialize(PackLock.Read(json)));
    }

    [Fact]
    public void Read_UnknownLockVersion_IsRefused()
    {
        var ex = Assert.Throws<PackFormatException>(() => PackLock.Read(Encoding.UTF8.GetBytes("""{ "lockVersion": 2 }""")));

        Assert.StartsWith("pack.lock.json has lockVersion 2;", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Problems_EveryDependencyLockedInRange_None()
    {
        Assert.Empty(PackLock.Create(_self, [_base, _parts]).Problems(Manifest(), _self.Digest));
    }

    [Fact]
    public void Problems_VersionOutsideTheRange_IsNamed()
    {
        PackLock locked = PackLock.Create(_self, [_base with { Version = "2.0.0" }, _parts]);

        PackProblem problem = Assert.Single(locked.Problems(Manifest(), _self.Digest));

        Assert.Equal("locks 'acme.base' at 2.0.0, outside '^1.2.0'", problem.Message);
    }

    [Fact]
    public void Problems_MissingDependencyAndUnsortedEntries_AreNamed()
    {
        PackLock locked = new() { LockVersion = 1, Pack = _self, Dependencies = [_parts, _parts] };

        IReadOnlyList<PackProblem> problems = locked.Problems(Manifest(), _self.Digest);

        Assert.Contains(problems, p => p.Message == "has no version of 'acme.base', which pack.json needs");
        Assert.Contains(problems, p => p.Message == "must be sorted by id, each id once");
    }

    private static PackManifest Manifest() => Manifests.Complete();
}
