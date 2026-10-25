using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanNamesDigestTests
{
    [Theory]
    [InlineData(@"D:\Projects\Line1")]
    [InlineData(@"d:/projects/line1.elk")]
    [InlineData(@"D:\Projects\电机+'&`<>\Line1.elk")]
    public void Digest_SameInputs_EqualsExistingCompilerDigest(string project)
    {
        PlanNameEntry[] names =
        [
            new() { Kind = "page", Name = "+A/1" },
            new() { Kind = "function", Name = "+A-K1" },
            new() { Kind = "function", Name = "+A-K1" },
            new() { Kind = "function", Name = "+a-K1" },
            new() { Kind = "function", Name = "电机 e\u0301 é 😀 \u2028\u2029\uFFFF\uD800" },
            new() { Kind = "function", Name = new string(Enumerable.Range(0, 128).Select(i => (char)i).ToArray()) },
        ];
        Assert.Equal(Existing(project, names), PlanNamesDigest.Names(project, names));
        Assert.Equal(PlanNamesDigest.Names(project, names), PlanNamesDigest.Names(project, names.Reverse()));
        Assert.Equal(Existing(project, []), PlanNamesDigest.Names(project, []));
    }

    [Fact]
    public void Digest_EveryUtf16CodeUnit_MatchesTheExistingEncoding()
    {
        PlanNameEntry[] names = [.. Enumerable.Range(0, 1024).Select(i => new PlanNameEntry
        {
            Kind = "function",
            Name = new string(Enumerable.Range(i * 64, 64).Select(c => (char)c).ToArray()),
        })];
        Assert.Equal(Existing(@"D:\P", names), PlanNamesDigest.Names(@"D:\P", names));
    }

    private static string Existing(string project, IEnumerable<PlanNameEntry> names) =>
        PlanSnapshotHashes.Names(project, names.Select(n => new PlanOccupiedName(n.Kind, n.Name)));
}
