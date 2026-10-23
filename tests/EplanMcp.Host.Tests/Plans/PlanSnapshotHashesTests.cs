using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanSnapshotHashesTests
{
    private const string Project = @"D:\Projects\Line1";

    [Fact]
    public void PageAndComposition_ReverseOrder_AgreeWithBridgeFormula()
    {
        SnapshotObject[] objects = [new() { Kind = "page", Type = 4, Oid = 7, Hash = "p" }, new() { Kind = "function", Type = 17, Oid = 22, Hash = "f" }];
        string hash = SnapshotCanonical.PageHash("standard", objects);
        Assert.Equal(hash, PlanSnapshotHashes.Page(objects.Reverse()));
        SnapshotPage[] pages = [new() { Type = 4, Oid = 7, Hash = hash }, new() { Type = 4, Oid = 9, Hash = "other" }];
        Assert.Equal(SnapshotCanonical.SnapshotHash("standard", pages), PlanSnapshotHashes.Compose(pages.Reverse()));
        Assert.NotEqual(PlanSnapshotHashes.Page(objects), PlanSnapshotHashes.Page(objects.Take(1)));
        Assert.NotEqual(PlanSnapshotHashes.Compose(pages), PlanSnapshotHashes.Compose(pages.Take(1)));
    }

    [Fact]
    public void Members_OrderAndDuplicateRows_AreSetsButContentAndExistenceMatter()
    {
        KeyValuePair<string, string>[] members = [new("page/4/7", "p"), new("function/17/22", "f")];
        string hash = PlanSnapshotHashes.Members(Project, members);
        Assert.Equal(hash, PlanSnapshotHashes.Members(Project, members.Reverse().Concat(members)));
        Assert.NotEqual(hash, PlanSnapshotHashes.Members(Project, members.Take(1)));
        Assert.NotEqual(hash, PlanSnapshotHashes.Members(Project, members.Append(new("function/17/23", "f"))));
        Assert.NotEqual(hash, PlanSnapshotHashes.Members(Project, [members[0], new("function/17/22", "changed")]));
        Assert.NotEqual(hash, PlanSnapshotHashes.Members(Project, [members[0], new("function/18/22", "f")]));
        Assert.NotEqual(hash, PlanSnapshotHashes.Members(Project, [members[0], new("connection/17/22", "f")]));
        Assert.NotEqual(PlanSnapshotHashes.Members(Project, []), PlanSnapshotHashes.Members(Project, members.Take(1)));
    }

    [Fact]
    public void Sets_EqualGuidAndObjectsInDifferentCopies_HaveDifferentProjectKeys()
    {
        KeyValuePair<string, string>[] members = [new("page/4/7", "same")];
        PlanOccupiedName[] names = [new("page", "+A1/1"), new("function", "+A1-K1")];
        Assert.Equal(PlanSnapshotHashes.Members(Project, members), PlanSnapshotHashes.Members(@"d:\projects\.\Line1.elk", members));
        Assert.NotEqual(PlanSnapshotHashes.Members(Project, members), PlanSnapshotHashes.Members(@"D:\Copy\Line1", members));
        Assert.NotEqual(PlanSnapshotHashes.Names(Project, names), PlanSnapshotHashes.Names(@"D:\Copy\Line1", names));
        Assert.Equal(@"D:\PROJECTS\LINE1.V1", PlanSnapshotHashes.ProjectKey(@"D:\Projects\Line1.v1"));
    }

    [Fact]
    public void Names_OrdinalSets_IncludeKindAndEscapeSeparators()
    {
        PlanOccupiedName[] names = [new("page", "+A1/1"), new("function", "+A1-K1")];
        string hash = PlanSnapshotHashes.Names(Project, names);
        Assert.Equal(hash, PlanSnapshotHashes.Names(Project, names.Reverse().Concat(names)));
        Assert.NotEqual(hash, PlanSnapshotHashes.Names(Project, names.Take(1)));
        Assert.NotEqual(hash, PlanSnapshotHashes.Names(Project, names.Append(new("function", "+A1-K2"))));
        Assert.NotEqual(hash, PlanSnapshotHashes.Names(Project, [names[0], new("function", "+a1-K1")]));
        Assert.NotEqual(PlanSnapshotHashes.Names(Project, [new("page", "x\ny")]), PlanSnapshotHashes.Names(Project, [new("page", "x"), new("page", "y")]));
    }
}
