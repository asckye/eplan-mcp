using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>Security review 2026-11-17: a write goes to the project of its plan, not to whatever carries the name later.</summary>
[Trait("Size", "Small")]
public sealed class PinnedProjectTests
{
    private static readonly WritePlan _plan = new() { Project = "Line1", ProjectPath = @"C:/scratch/Line1" };

    [Fact]
    public void AProjectGivenByName_BecomesThePlansPath()
    {
        var args = new PageCreateArgs { Project = "Line1" };
        Assert.Same(args, PinnedProject.In(args, _plan));
        Assert.Equal(_plan.ProjectPath, args.Project);
    }

    [Fact]
    public void APlanWithoutAProject_LeavesTheArgumentsAlone()
    {
        var args = new PageCreateArgs { Project = "Line1" };
        PinnedProject.In(args, new WritePlan());
        Assert.Equal("Line1", args.Project);
    }

    [Fact]
    public void ArgumentsWithoutAProject_AreLeftAlone()
    {
        var args = new ProjectOpenArgs { Path = @"C:/scratch/Line1.elk" };
        Assert.Same(args, PinnedProject.In(args, _plan));
        Assert.Equal(@"C:/scratch/Line1.elk", args.Path);
    }

    /// <summary>Every execute argument type of the contracts that names a project can be pinned: a string the pipeline can set.</summary>
    [Fact]
    public void EveryArgumentsTypeWithAProject_HasASettableStringProject()
    {
        Type[] odd = [.. typeof(PageCreateArgs).Assembly.GetTypes()
            .Where(t => t.IsClass && t.Name.EndsWith("Args", StringComparison.Ordinal) && t.GetProperty("Project") is { } p && (p.PropertyType != typeof(string) || !p.CanWrite))];
        Assert.Empty(odd);
    }
}
