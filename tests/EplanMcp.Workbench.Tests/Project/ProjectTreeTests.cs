using System.Text.Json.Nodes;
using EplanMcp.Workbench.Project;

namespace EplanMcp.Workbench.Tests.Project;

/// <summary>The project tree of the Workbench 4.0 design, from eplan_page_list: groups by structure, folders by type.</summary>
[Trait("Size", "Small")]
public sealed class ProjectTreeTests
{
    [Theory]
    [InlineData("=GAA+A1&EFS1/4", "=GAA+A1", "EFS")]
    [InlineData("=GAA+A2&EPL1/1", "=GAA+A2", "EPL")]
    [InlineData("&EMA/1", "", "EMA")]
    [InlineData("1", "", "")]
    [InlineData("=A&TOOLONG1", "=A", "")]
    public void APageName_GivesItsGroupAndItsKind(string name, string prefix, string kind) =>
        Assert.Equal((prefix, kind), (ProjectTree.Prefix(name), ProjectTree.Kind(name)));

    [Fact]
    public void ThePages_AreGroupedByStructure_ThenByType_InTheOrderTheyCame()
    {
        var list = new JsonObject
        {
            ["pages"] = new JsonArray(
                Page("=GAA+A1&EFS1/1", "Circuit", "Mains"),
                Page("=GAA+A2&EPL1/1", "PanelLayout", "Panel"),
                Page("=GAA+A1&EFA1/2", "CircuitFluid", "Safety"),
                Page("=GAA+A1&EFS1/4", "Circuit", "Drive")),
        };

        IReadOnlyList<ProjectGroup> groups = ProjectTree.Build(ProjectTree.Pages(list));

        Assert.Equal(["=GAA+A1", "=GAA+A2"], groups.Select(g => g.Prefix));
        Assert.Equal(["Circuit", "CircuitFluid"], groups[0].Folders.Select(f => f.Type));
        Assert.Equal(["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/4"], groups[0].Folders[0].Pages.Select(p => p.Name));
        Assert.Equal(3, groups[0].PageCount);
    }

    [Fact]
    public void TextsFromTheProject_ArePlain()
    {
        var list = new JsonObject { ["pages"] = new JsonArray(Page("=A&EFS1/1", "Circuit", "Motor\u202Egnp.exe\nnext")) };

        ProjectPageNode page = Assert.Single(ProjectTree.Pages(list));

        Assert.DoesNotContain('\u202E', page.Description);
        Assert.DoesNotContain('\n', page.Description);
    }

    private static JsonObject Page(string name, string type, string description) =>
        new() { ["name"] = name, ["type"] = type, ["description"] = description };
}
