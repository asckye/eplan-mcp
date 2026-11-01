using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;

namespace EplanMcp.Coverage.Tests.Reporting;

[Trait("Size", "Medium")]
public sealed class ToolReachCoverageTests
{
    [Fact]
    public void Build_ReviewedAliases_CountOnlyExplicitMembersAndTools_WithoutDuplicatingScanUsage()
    {
        using TempRepository repository = Create();
        CoverageReport before = CoverageReport.Build(repository.Root);
        WriteReach(repository);

        CoverageReport after = CoverageReport.Build(repository.Root);
        ToolMapping mapping = ToolMapping.Build(repository.Root);
        UnitCoverage alias = Assert.Single(after.Units, u => u.Unit == "O:Eplan.EplApi.Test.Page.Alias");
        Assert.Equal(before.Layers["operation"].Implemented + 1, after.Layers["operation"].Implemented);
        Assert.Equal(before.Layers["operation"].Denominator, after.Layers["operation"].Denominator);
        Assert.Equal(["eplan_page_list"], alias.Tools);
        Assert.True(alias.Partial);
        Assert.Contains("P:Eplan.EplApi.Test.Page.Alias(System.Int32)", mapping.Tools.Single(t => t.Name == "eplan_page_list").Api);
        Assert.DoesNotContain("P:Eplan.EplApi.Test.Page.Alias(System.String)", mapping.Tools.SelectMany(t => t.Api));
        Assert.Equivalent(before.Layers["data_dictionary"], after.Layers["data_dictionary"]);
        Assert.Equivalent(before.Layers["action"], after.Layers["action"]);
        ReportFixture.AddUsages(repository, ("P:Eplan.EplApi.Test.Page.Alias(System.Int32)", ReportFixture.GeneratedSite("page.list")));
        Assert.Equivalent(after.Layers["operation"], CoverageReport.Build(repository.Root).Layers["operation"]);
    }

    [Theory]
    [InlineData("O:Eplan.EplApi.Test.Page.Gone", "P:Eplan.EplApi.Test.Page.Alias(System.Int32)", "eplan_page_list")]
    [InlineData("O:Eplan.EplApi.Test.Page.Name", "P:Eplan.EplApi.Test.Page.Gone", "eplan_page_list")]
    [InlineData("O:Eplan.EplApi.Test.Page.Name", "P:Eplan.EplApi.Test.Page.Alias(System.Int32)", "eplan_page_create")]
    [InlineData("O:Eplan.EplApi.Test.Page.Name", "P:Eplan.EplApi.Test.Page.Alias(System.Int32)", "eplan_gone")]
    public void Build_UnknownMemberOrUnreachedViaOrTool_Fails(string via, string member, string tool)
    {
        using TempRepository repository = Create();
        WriteReach(repository, via, member, tool);

        Assert.Throws<InvalidDataException>(() => ToolMapping.Build(repository.Root));
    }

    private static TempRepository Create()
    {
        TempRepository repository = ReportFixture.Create();
        const string Path = "catalog/eplan-2026.0.3/members/Eplan.EplApi.Test.jsonl";
        return repository.Write(Path, repository.Read(Path) + "\n" + """
            {"id":"P:Eplan.EplApi.Test.Page.Alias(System.Int32)","kind":"property","layer":"operation","type":"T:Eplan.EplApi.Test.Page","group":"O:Eplan.EplApi.Test.Page.Alias"}
            {"id":"P:Eplan.EplApi.Test.Page.Alias(System.String)","kind":"property","layer":"operation","type":"T:Eplan.EplApi.Test.Page","group":"O:Eplan.EplApi.Test.Page.Alias"}

            """);
    }

    private static void WriteReach(TempRepository repository, string via = "O:Eplan.EplApi.Test.Page.Name",
        string member = "P:Eplan.EplApi.Test.Page.Alias(System.Int32)", string tool = "eplan_page_list") =>
        repository.Write(Overlay.RelativePath, repository.Read(Overlay.RelativePath) + "\n" + $$"""
            reached-by-tool:
              - { match: "O:Eplan.EplApi.Test.Page.Alias", members: ["{{member}}"], tools: [{{tool}}], via: "{{via}}", source: src/A.cs:1, receiver: src/B.cs:2 }

            """);
}
