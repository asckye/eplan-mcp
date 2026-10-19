using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ProjectResolverTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2" };
    private static readonly OpenProject _line1Copy = new() { Name = "Line1", Path = @"E:\Archive\Line1" };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Omitted_IsTheProjectSelectedInEplan(string? requested)
    {
        Assert.Same(_line1, ProjectResolver.Resolve([_line2, _line1], requested));
    }

    [Fact]
    public void Omitted_NoneSelectedButOnlyOneOpen_IsThatOne()
    {
        Assert.Same(_line2, ProjectResolver.Resolve([_line2], null));
    }

    [Fact]
    public void Omitted_NoneSelectedAndSeveralOpen_IsAmbiguousWithEveryPath()
    {
        BridgeError error = Fails(() => ProjectResolver.Resolve([_line2, _line1Copy], null));

        Assert.Equal(ErrorReason.AmbiguousName, error.Reason);
        Assert.Equal(@"D:\Projects\Line2" + "\n" + @"E:\Archive\Line1", error.Metadata!["candidates"]);
    }

    [Fact]
    public void NothingOpen_IsProjectNotOpen()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(() => ProjectResolver.Resolve([], null)).Reason);
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(() => ProjectResolver.Resolve([], "Line1")).Reason);
    }

    [Theory]
    [InlineData(@"D:\Projects\Line2")]
    [InlineData(@"D:\Projects\Line2.elk")]
    [InlineData(@"d:/projects/LINE2.ELK")]
    [InlineData(@" D:\Projects\Line2\ ")]
    [InlineData("line2")]
    public void ByPathOrName_FindsTheProject(string requested)
    {
        Assert.Same(_line2, ProjectResolver.Resolve([_line1, _line2], requested));
    }

    [Fact]
    public void PathWins_WhenTwoProjectsShareTheName()
    {
        Assert.Same(_line1Copy, ProjectResolver.Resolve([_line1, _line1Copy], @"E:\Archive\Line1.elk"));
    }

    [Fact]
    public void SharedName_IsAmbiguousAndListsBothPaths()
    {
        BridgeError error = Fails(() => ProjectResolver.Resolve([_line1, _line2, _line1Copy], "Line1"));

        Assert.Equal(ErrorReason.AmbiguousName, error.Reason);
        Assert.Equal("Line1", error.Metadata!["name"]);
        Assert.Equal(["D:\\Projects\\Line1", "E:\\Archive\\Line1"], error.Metadata["candidates"].Split('\n'));
        Assert.DoesNotContain(@"\", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_IsProjectNotOpenWithAHintAndTheValueInMetadata()
    {
        BridgeError error = Fails(() => ProjectResolver.Resolve([_line1], "Line9"));

        Assert.Equal(ErrorReason.ProjectNotOpen, error.Reason);
        Assert.Contains("eplan_project_list", error.Message, StringComparison.Ordinal);
        Assert.Equal("Line9", error.Metadata!["project"]);
    }

    [Fact]
    public void Ambiguous_ListsAtMostTwentyCandidatesButCountsAll()
    {
        BridgeError error = ProjectResolver.Ambiguous("device", "-K1", Enumerable.Range(1, 25).Select(i => "=A" + i + "-K1"), "m").Error;

        Assert.Equal(ProjectResolver.MaxCandidates, error.Metadata!["candidates"].Split('\n').Length);
        Assert.Equal("25", error.Metadata["candidateCount"]);
        Assert.Equal("device", error.Metadata["kind"]);
    }

    private static BridgeError Fails(Func<OpenProject> resolve) => Assert.Throws<BridgeErrorException>(() => resolve()).Error;
}
